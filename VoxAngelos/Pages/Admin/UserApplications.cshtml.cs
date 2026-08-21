using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class UserApplicationsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly SensitiveMediaRetentionService _mediaRetention;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<UserApplicationsModel> _logger;

        public UserApplicationsModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            SensitiveMediaRetentionService mediaRetention,
            IEmailSender emailSender,
            ILogger<UserApplicationsModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
            _mediaRetention = mediaRetention;
            _emailSender = emailSender;
            _logger = logger;
        }

        public List<CitizenApplicationViewModel> Applications { get; set; } = new();

        public string FilterStatus { get; set; } = "All";
        public string FaceMatchFilter { get; set; } = "All";
        public string DateRange { get; set; } = "All";
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string SortOrder { get; set; } = "Newest";

        public async Task OnGetAsync(
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string dateRange = "All",
            DateOnly? dateFrom = null,
            DateOnly? dateTo = null,
            string sortOrder = "Newest")
        {
            FilterStatus = new[] { "All", "Pending", "Approved", "Rejected" }.Contains(filterStatus)
                ? filterStatus : "All";
            FaceMatchFilter = faceMatchFilter;
            DateRange = new[] { "All", "Week", "Month", "Year" }.Contains(dateRange)
                ? dateRange : "All";
            DateFrom = dateFrom;
            DateTo = dateTo;
            if (DateFrom.HasValue && DateTo.HasValue && DateFrom > DateTo)
                (DateFrom, DateTo) = (DateTo, DateFrom);
            SortOrder = string.Equals(sortOrder, "Oldest", StringComparison.OrdinalIgnoreCase)
                ? "Oldest" : "Newest";

            var citizenUsers = await _userManager.GetUsersInRoleAsync("User");

            // GetUsersInRoleAsync has already materialized the users; continue with
            // in-memory filtering so Manila-local calendar rules can be applied safely.
            var query = citizenUsers.AsEnumerable();

            if (FilterStatus != "All")
                query = query.Where(u => u.ApprovalStatus == FilterStatus);

            var manilaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, manilaZone));
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

            DateOnly ManilaDate(DateTime createdAt)
            {
                var utc = createdAt.Kind == DateTimeKind.Utc
                    ? createdAt
                    : DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, manilaZone));
            }

            bool MatchesDate(DateTime createdAt)
            {
                var appliedDate = ManilaDate(createdAt);
                if (DateFrom.HasValue || DateTo.HasValue)
                {
                    var from = DateFrom ?? DateTo!.Value;
                    var to = DateTo ?? DateFrom!.Value;
                    return appliedDate >= from && appliedDate <= to;
                }

                return DateRange switch
                {
                    "Week" => appliedDate >= weekStart && appliedDate <= today,
                    "Month" => appliedDate.Year == today.Year && appliedDate.Month == today.Month,
                    "Year" => appliedDate.Year == today.Year,
                    _ => true
                };
            }

            query = query.Where(u => MatchesDate(u.CreatedAt));

            var userIds = query.Select(u => u.Id).ToList();

            var profiles = await _context.UserProfiles
                .Where(p => userIds.Contains(p.UserId))
                .ToListAsync();

            var faceVerifications = await _context.UserFaceVerifications
                .Where(f => userIds.Contains(f.UserId))
                .ToListAsync();

            foreach (var user in query)
            {
                var profile = profiles.FirstOrDefault(p => p.UserId == user.Id);
                var face = faceVerifications.FirstOrDefault(f => f.UserId == user.Id);

                Applications.Add(new CitizenApplicationViewModel
                {
                    UserId = user.Id,
                    FullName = profile != null
                        ? $"{profile.FirstName} {profile.MiddleName} {profile.LastName}".Trim()
                        : user.Email,
                    Email = user.Email,
                    ContactNumber = user.PhoneNumber ?? "N/A",
                    DateApplied = ManilaDate(user.CreatedAt).ToDateTime(TimeOnly.FromDateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(
                            user.CreatedAt.Kind == DateTimeKind.Utc ? user.CreatedAt : DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
                            manilaZone))),
                    ApprovalStatus = user.ApprovalStatus,
                    FaceMatchConfidence = FaceMatchConfidenceScale.Normalize(face?.MatchConfidence),
                    HasFaceVerification = face != null
                });
            }

            if (faceMatchFilter == "Attention")
            {
                Applications = Applications
                    .Where(a => !a.HasFaceVerification || a.FaceMatchConfidence < 0.50m)
                    .ToList();
            }

            if (_environment.IsDevelopment()
                && faceMatchFilter != "Attention"
                && (filterStatus == "All" || filterStatus == "Pending"))
            {
                var mockCreatedAt = DateTime.UtcNow.AddDays(-1);
                if (MatchesDate(mockCreatedAt)) Applications.Add(new CitizenApplicationViewModel
                {
                    UserId = ReviewApplicationModel.DevelopmentMockCitizenId,
                    FullName = "Mock Citizen (Development)",
                    Email = "mock.citizen@example.test",
                    ContactNumber = "(000) 000-0000",
                    DateApplied = TimeZoneInfo.ConvertTimeFromUtc(mockCreatedAt, manilaZone),
                    ApprovalStatus = "Pending",
                    FaceMatchConfidence = 0.87m,
                    HasFaceVerification = true,
                    IsMock = true
                });
            }

            Applications = SortOrder == "Oldest"
                ? Applications.OrderBy(a => a.DateApplied).ToList()
                : Applications.OrderByDescending(a => a.DateApplied).ToList();
        }

        public async Task<IActionResult> OnPostApproveAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.ApprovalStatus == "Pending")
            {
                user.ApprovalStatus = "Approved";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    TempData["AdminError"] = "Could not update this application — it may have just been changed by another admin.";
                else
                {
                    await TryPurgeSensitiveMediaAsync(userId);
                    TempData["AdminSuccess"] = "The citizen account was approved successfully.";
                }
            }
            else
                TempData["AdminError"] = user == null
                    ? "The citizen account could not be found."
                    : "This application has already received a final decision and cannot be changed.";
            return RedirectToPage(new { filterStatus = FilterStatus });
        }

        public async Task<IActionResult> OnPostRejectAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.ApprovalStatus == "Pending")
            {
                user.ApprovalStatus = "Rejected";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    TempData["AdminError"] = "Could not update this application — it may have just been changed by another admin.";
                else
                {
                    await TryPurgeSensitiveMediaAsync(userId);
                    TempData["AdminSuccess"] = "The citizen account was rejected successfully.";
                }
            }
            else
                TempData["AdminError"] = user == null
                    ? "The citizen account could not be found."
                    : "This application has already received a final decision and cannot be changed.";
            return RedirectToPage(new { filterStatus = FilterStatus });
        }

        public Task<IActionResult> OnPostBulkApproveAsync(
            string[] userIds,
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string dateRange = "All",
            DateOnly? dateFrom = null,
            DateOnly? dateTo = null,
            string sortOrder = "Newest") =>
            ApplyBulkDecisionAsync(userIds, "Approved", filterStatus, faceMatchFilter, dateRange, dateFrom, dateTo, sortOrder);

        public Task<IActionResult> OnPostBulkRejectAsync(
            string[] userIds,
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string dateRange = "All",
            DateOnly? dateFrom = null,
            DateOnly? dateTo = null,
            string sortOrder = "Newest") =>
            ApplyBulkDecisionAsync(userIds, "Rejected", filterStatus, faceMatchFilter, dateRange, dateFrom, dateTo, sortOrder);

        private async Task<IActionResult> ApplyBulkDecisionAsync(
            string[] userIds,
            string decision,
            string filterStatus,
            string faceMatchFilter,
            string dateRange,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            string sortOrder)
        {
            var decided = 0;
            var skipped = 0;
            var notificationFailures = 0;
            var isApproval = decision == "Approved";

            foreach (var userId in (userIds ?? Array.Empty<string>())
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal)
                         .Take(100))
            {
                if (userId == ReviewApplicationModel.DevelopmentMockCitizenId)
                {
                    skipped++;
                    continue;
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null || user.ApprovalStatus != "Pending" || !await _userManager.IsInRoleAsync(user, "User"))
                {
                    skipped++;
                    continue;
                }

                user.ApprovalStatus = decision;
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    skipped++;
                    continue;
                }

                await TryPurgeSensitiveMediaAsync(userId);
                decided++;
                _logger.LogInformation("Admin bulk-{Decision} citizen {UserId}", decision.ToLowerInvariant(), userId);

                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    try
                    {
                        await SendDecisionEmailAsync(user.Email, isApproval);
                    }
                    catch (Exception ex)
                    {
                        notificationFailures++;
                        _logger.LogWarning(ex, "Bulk decision email failed for citizen {UserId}", userId);
                    }
                }
            }

            if (decided == 0)
                TempData["AdminError"] = skipped == 0
                    ? "No applications were selected."
                    : "None of the selected applications could be changed. They may already have a final decision.";
            else
            {
                var action = isApproval ? "approved" : "rejected";
                TempData["AdminSuccess"] = $"{decided} application(s) {action}." +
                    (skipped > 0 ? $" {skipped} skipped because they were already decided or unavailable." : "") +
                    (notificationFailures > 0 ? $" {notificationFailures} email notification(s) could not be sent." : " Applicants were notified.");
            }

            return RedirectToPage(new
            {
                filterStatus,
                faceMatchFilter,
                dateRange,
                dateFrom = dateFrom?.ToString("yyyy-MM-dd"),
                dateTo = dateTo?.ToString("yyyy-MM-dd"),
                sortOrder
            });
        }

        private Task SendDecisionEmailAsync(string email, bool approved)
        {
            var subject = approved
                ? "Your Vox Angelos Account Has Been Approved"
                : "Your Vox Angelos Account Application";
            var heading = approved ? "Account Approved!" : "Application Update";
            var color = approved ? "#2e7d32" : "#c62828";
            var message = approved
                ? "Your Vox Angelos citizen account has been reviewed and approved. You can now log in and use the platform."
                : "Your Vox Angelos citizen account application was rejected because it did not meet the verification requirements.";

            return _emailSender.SendEmailAsync(
                email,
                subject,
                $"<div style='font-family:Arial,sans-serif;max-width:480px;margin:0 auto'>" +
                $"<h2 style='color:{color}'>{heading}</h2><p>Hello,</p><p>{message}</p>" +
                "<p style='color:#888;font-size:.85rem'>— The Vox Angelos Team</p></div>");
        }

        private async Task TryPurgeSensitiveMediaAsync(string userId)
        {
            try
            {
                var purgedCount = await _mediaRetention.PurgeUserMediaAsync(
                    userId,
                    HttpContext.RequestAborted);
                _logger.LogInformation(
                    "Immediate review cleanup purged {Count} protected media copy/copies for citizen {UserId}.",
                    purgedCount,
                    userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Immediate protected-media cleanup failed for citizen {UserId}; the retention sweep will retry.",
                    userId);
            }
        }
    }

    public class CitizenApplicationViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public DateTime DateApplied { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public decimal? FaceMatchConfidence { get; set; }
        public bool HasFaceVerification { get; set; }
        public bool IsMock { get; set; }
    }
}
