using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public int UnreviewedProjects { get; set; }
        public int UnverifiedAccounts { get; set; }
        public int VerifiedAccounts { get; set; }

        // ================= NEW: everything below this line is additive =================

        // Below this face-match confidence (%), a verification attempt counts as a "failure".
        private const int FaceMatchFailureThreshold = 50;

        public int RejectedAccounts { get; set; }

        public List<QueueItemViewModel> OldestPending { get; set; } = new();
        public int TotalFaceChecks { get; set; }
        public int LowFaceMatchCount { get; set; }
        public double FaceMatchFailureRate { get; set; }
        public List<DailyCountViewModel> ApplicationsLast14Days { get; set; } = new();

        public int TotalLguAccounts { get; set; }
        public int ActiveLguAccounts { get; set; }
        public int DisabledLguAccounts { get; set; }
        public List<DepartmentAccountViewModel> AccountsByDepartment { get; set; } = new();
        public List<QueueItemViewModel> RecentLguAccounts { get; set; } = new();

        public int NlpTotalReviewed { get; set; }
        public double NlpAccuracyPercent { get; set; }
        public bool NlpThresholdMet { get; set; }
        public string? WorstDepartment { get; set; }
        public double WorstDepartmentAccuracy { get; set; }
        public int RecentMisclassificationCount { get; set; }

        public async Task OnGetAsync()
        {
            // Count pending citizen accounts
            var allUsers = await _userManager.GetUsersInRoleAsync("User");
            UnverifiedAccounts = allUsers.Count(u => u.ApprovalStatus == "Pending");
            VerifiedAccounts = allUsers.Count(u => u.ApprovalStatus == "Approved");

            // Projects — placeholder for now
            UnreviewedProjects = 0;

            // ---------- NEW: additional dashboard widgets ----------
            RejectedAccounts = allUsers.Count(u => u.ApprovalStatus == "Rejected");

            await LoadApplicationsWidgetsAsync(allUsers);
            await LoadOfficeManagementWidgetsAsync();
            await LoadNlpWidgetsAsync();
        }

        private async Task LoadApplicationsWidgetsAsync(IList<ApplicationUser> allUsers)
        {
            var pendingUsers = allUsers
                .Where(u => u.ApprovalStatus == "Pending")
                .OrderBy(u => u.CreatedAt)
                .Take(5)
                .ToList();

            var pendingUserIds = pendingUsers.Select(u => u.Id).ToList();

            var pendingProfiles = await _context.UserProfiles
                .Where(p => pendingUserIds.Contains(p.UserId))
                .ToListAsync();

            OldestPending = pendingUsers
                .Select(u =>
                {
                    var profile = pendingProfiles.FirstOrDefault(p => p.UserId == u.Id);
                    return new QueueItemViewModel
                    {
                        UserId = u.Id,
                        Label = profile != null
                            ? $"{profile.FirstName} {profile.LastName}".Trim()
                            : (u.Email ?? "Unknown"),
                        SubLabel = u.Email ?? string.Empty,
                        Timestamp = u.CreatedAt
                    };
                })
                .ToList();

            var allUserIds = allUsers.Select(u => u.Id).ToList();
            var faceChecks = await _context.UserFaceVerifications
                .Where(f => allUserIds.Contains(f.UserId))
                .ToListAsync();

            TotalFaceChecks = faceChecks.Count;
            LowFaceMatchCount = faceChecks.Count(f => f.MatchConfidence < FaceMatchFailureThreshold);
            FaceMatchFailureRate = TotalFaceChecks == 0
                ? 0
                : Math.Round(LowFaceMatchCount * 100.0 / TotalFaceChecks, 1);

            // 14-day trend, zero-filled so the chart has no missing days
            var since = DateTime.UtcNow.Date.AddDays(-13);
            var grouped = allUsers
                .Where(u => u.CreatedAt.Date >= since)
                .GroupBy(u => u.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            var trend = new List<DailyCountViewModel>();
            for (var day = since; day <= DateTime.UtcNow.Date; day = day.AddDays(1))
            {
                trend.Add(new DailyCountViewModel
                {
                    Date = day,
                    Count = grouped.TryGetValue(day, out var c) ? c : 0
                });
            }
            ApplicationsLast14Days = trend;
        }

        private async Task LoadOfficeManagementWidgetsAsync()
        {
            var lguUsers = await _userManager.GetUsersInRoleAsync("LGU");

            bool IsActive(ApplicationUser u) => u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.UtcNow;

            TotalLguAccounts = lguUsers.Count;
            ActiveLguAccounts = lguUsers.Count(IsActive);
            DisabledLguAccounts = TotalLguAccounts - ActiveLguAccounts;

            AccountsByDepartment = lguUsers
                .GroupBy(u => u.Department ?? "Unassigned")
                .Select(g => new DepartmentAccountViewModel
                {
                    Department = g.Key,
                    Total = g.Count(),
                    Active = g.Count(IsActive),
                    Disabled = g.Count(u => !IsActive(u))
                })
                .OrderByDescending(d => d.Total)
                .ToList();

            RecentLguAccounts = lguUsers
                .OrderByDescending(u => u.CreatedAt)
                .Take(5)
                .Select(u => new QueueItemViewModel
                {
                    UserId = u.Id,
                    Label = u.Department ?? "Unassigned",
                    SubLabel = u.EmployeeId ?? "N/A",
                    Timestamp = u.CreatedAt
                })
                .ToList();
        }

        private async Task LoadNlpWidgetsAsync()
        {
            var corrections = await _context.ClassificationCorrections.ToListAsync();

            NlpTotalReviewed = corrections.Count;
            var correctCount = corrections.Count(c => c.WasCorrect);
            NlpAccuracyPercent = NlpTotalReviewed == 0
                ? 0
                : Math.Round(correctCount * 100.0 / NlpTotalReviewed, 1);
            NlpThresholdMet = NlpAccuracyPercent >= NlpAccuracyModel.TargetAccuracyPercent;

            if (NlpTotalReviewed > 0)
            {
                var worst = corrections
                    .GroupBy(c => c.CorrectedCategory)
                    .Select(g => new
                    {
                        Department = g.Key,
                        Accuracy = Math.Round(g.Count(c => c.WasCorrect) * 100.0 / g.Count(), 1)
                    })
                    .OrderBy(d => d.Accuracy)
                    .FirstOrDefault();

                if (worst != null)
                {
                    WorstDepartment = worst.Department;
                    WorstDepartmentAccuracy = worst.Accuracy;
                }
            }

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            RecentMisclassificationCount = corrections
                .Count(c => !c.WasCorrect && c.ReviewedAt >= sevenDaysAgo);
        }
    }

    public class QueueItemViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SubLabel { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int DaysAgo => (DateTime.UtcNow.Date - Timestamp.Date).Days;
    }

    public class DailyCountViewModel
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class DepartmentAccountViewModel
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Active { get; set; }
        public int Disabled { get; set; }
    }
}