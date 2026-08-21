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
        private readonly IConfiguration _configuration;

        public IndexModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        public int UnreviewedProjects { get; set; }
        public int UnverifiedAccounts { get; set; }
        public int VerifiedAccounts { get; set; }

        // ================= NEW: everything below this line is additive =================

        // Confidence is stored from 0 to 1, so 0.50 represents the 50% review threshold.
        private const decimal FaceMatchFailureThreshold = 0.50m;

        public int RejectedAccounts { get; set; }

        public List<QueueItemViewModel> OldestPending { get; set; } = new();
        public int TotalFaceChecks { get; set; }
        public int LowFaceMatchCount { get; set; }
        public int PassedFaceMatchCount => TotalFaceChecks - LowFaceMatchCount;
        public int MissingFaceMatchCount { get; set; }
        public int FaceMatchAttentionCount => LowFaceMatchCount + MissingFaceMatchCount;
        public double FaceMatchFailureRate { get; set; }
        public List<DailyCountViewModel> ApplicationsLast14Days { get; set; } = new();

        public int TotalLguAccounts { get; set; }
        public int ActiveLguAccounts { get; set; }
        public int DisabledLguAccounts { get; set; }
        public int ConfiguredDepartmentCount { get; set; }
        public int RoutingGapCount { get; set; }
        public List<DepartmentAccountViewModel> AccountsByDepartment { get; set; } = new();

        public bool AdminTriageEnabled =>
            _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized");
        public int UncategorizedConcernCount { get; set; }
        public int UncategorizedRecommendationCount { get; set; }
        public int TotalAwaitingRouting => UncategorizedConcernCount + UncategorizedRecommendationCount;
        public DateTime? OldestUncategorizedSubmittedAt { get; set; }
        public RoutingVolumeViewModel? MostAdminRoutedDepartment { get; set; }
        public RoutingVolumeViewModel? LeastAdminRoutedDepartment { get; set; }
        public int AdminRoutedDepartmentCount { get; set; }
        public string OldestRoutingAge
        {
            get
            {
                if (!OldestUncategorizedSubmittedAt.HasValue) return "—";
                var age = DateTime.UtcNow - OldestUncategorizedSubmittedAt.Value;
                if (age.TotalDays >= 1) return $"{Math.Max(1, (int)age.TotalDays)}d";
                if (age.TotalHours >= 1) return $"{Math.Max(1, (int)age.TotalHours)}h";
                return $"{Math.Max(1, (int)age.TotalMinutes)}m";
            }
        }

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
            await LoadRoutingWidgetsAsync();
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
            LowFaceMatchCount = faceChecks.Count(f =>
                VoxAngelos.Services.FaceMatchConfidenceScale.Normalize(f.MatchConfidence) < FaceMatchFailureThreshold);
            MissingFaceMatchCount = allUserIds.Count - faceChecks
                .Select(f => f.UserId)
                .Distinct()
                .Count();
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
                    DepartmentFullName = g.Select(user => user.DepartmentFullName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Office name not provided",
                    Total = g.Count(),
                    Active = g.Count(IsActive),
                    Disabled = g.Count(u => !IsActive(u)),
                    CategoryCount = g.SelectMany(user => user.Categories ?? new List<string>())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    KeywordCount = g.SelectMany(user => user.Tags ?? new List<string>())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                })
                .OrderBy(department => department.Department)
                .ToList();
            ConfiguredDepartmentCount = AccountsByDepartment.Count(department => department.Department != "Unassigned");
            RoutingGapCount = AccountsByDepartment.Count(department =>
                department.Active == 0 || department.CategoryCount == 0 || department.KeywordCount == 0);
        }

        private async Task LoadRoutingWidgetsAsync()
        {
            if (AdminTriageEnabled)
            {
                var concernDates = await _context.Concerns
                    .AsNoTracking()
                    .Where(concern => concern.Status == "Unresolved" &&
                        (concern.Category == null || concern.Category == ""))
                    .Select(concern => concern.SubmittedAt)
                    .ToListAsync();
                var recommendationDates = await _context.Recommendations
                    .AsNoTracking()
                    .Where(recommendation => recommendation.Status == "Pending" &&
                        (recommendation.AssignedOffice == null || recommendation.AssignedOffice == ""))
                    .Select(recommendation => recommendation.SubmittedAt)
                    .ToListAsync();

                UncategorizedConcernCount = concernDates.Count;
                UncategorizedRecommendationCount = recommendationDates.Count;
                OldestUncategorizedSubmittedAt = concernDates
                    .Concat(recommendationDates)
                    .OrderBy(date => date)
                    .Cast<DateTime?>()
                    .FirstOrDefault();

                var routingByDepartment = await _context.AdminRoutingAssignments
                    .AsNoTracking()
                    .GroupBy(assignment => assignment.Department)
                    .Select(group => new RoutingVolumeViewModel
                    {
                        Department = group.Key,
                        Total = group.Count(),
                        Concerns = group.Count(assignment => assignment.SubmissionType == "Concern"),
                        Recommendations = group.Count(assignment => assignment.SubmissionType == "Recommendation")
                    })
                    .ToListAsync();

                AdminRoutedDepartmentCount = routingByDepartment.Count;
                MostAdminRoutedDepartment = routingByDepartment
                    .OrderByDescending(item => item.Total)
                    .ThenBy(item => item.Department)
                    .FirstOrDefault();
                LeastAdminRoutedDepartment = routingByDepartment
                    .OrderBy(item => item.Total)
                    .ThenBy(item => item.Department)
                    .FirstOrDefault();
            }
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

    public class RoutingVolumeViewModel
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Concerns { get; set; }
        public int Recommendations { get; set; }
    }

    public class DepartmentAccountViewModel
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Active { get; set; }
        public int Disabled { get; set; }
        public string DepartmentFullName { get; set; } = string.Empty;
        public int CategoryCount { get; set; }
        public int KeywordCount { get; set; }
        public bool RoutingReady => Active > 0 && CategoryCount > 0 && KeywordCount > 0;
    }
}
