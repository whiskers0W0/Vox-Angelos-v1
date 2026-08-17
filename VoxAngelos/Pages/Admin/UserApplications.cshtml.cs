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
        public string SearchTerm { get; set; } = string.Empty;
        public string SortOrder { get; set; } = "newest";

        public async Task OnGetAsync(
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string? search = null,
            string? sort = null)
        {
            FilterStatus = filterStatus;
            FaceMatchFilter = faceMatchFilter;
            SearchTerm = search?.Trim() ?? string.Empty;
            SortOrder = string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase)
                ? "oldest"
                : "newest";

            var citizenUsers = await _userManager.GetUsersInRoleAsync("User");

            var query = citizenUsers.AsQueryable();

            if (filterStatus != "All")
                query = query.Where(u => u.ApprovalStatus == filterStatus);

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
                    DateApplied = user.CreatedAt,
                    ApprovalStatus = user.ApprovalStatus,
                    FaceMatchConfidence = face?.MatchConfidence,
                    HasFaceVerification = face != null
                });
            }

            if (faceMatchFilter == "Attention")
            {
                Applications = Applications
                    .Where(a => !a.HasFaceVerification || a.FaceMatchConfidence < 0.50m)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                Applications = Applications
                    .Where(a =>
                        a.FullName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                        a.Email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                        a.ContactNumber.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            Applications = (SortOrder == "oldest"
                ? Applications.OrderBy(a => a.DateApplied)
                : Applications.OrderByDescending(a => a.DateApplied))
                .ToList();

            if (_environment.IsDevelopment()
                && faceMatchFilter != "Attention"
                && (filterStatus == "All" || filterStatus == "Pending"))
            {
                Applications.Add(new CitizenApplicationViewModel
                {
                    UserId = ReviewApplicationModel.DevelopmentMockCitizenId,
                    FullName = "Mock Citizen (Development)",
                    Email = "mock.citizen@example.test",
                    ContactNumber = "(000) 000-0000",
                    DateApplied = DateTime.UtcNow.AddDays(-1),
                    ApprovalStatus = "Pending",
                    FaceMatchConfidence = 0.87m,
                    HasFaceVerification = true,
                    IsMock = true
                });
            }
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

        public async Task<IActionResult> OnPostBulkApproveAsync(
            string[] userIds,
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string? search = null,
            string? sort = null)
        {
            var approved = 0;
            var skipped = 0;

            foreach (var userId in userIds ?? Array.Empty<string>())
            {
                if (userId == ReviewApplicationModel.DevelopmentMockCitizenId)
                {
                    skipped++;
                    continue;
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null || user.ApprovalStatus != "Pending")
                {
                    skipped++;
                    continue;
                }

                user.ApprovalStatus = "Approved";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    skipped++;
                    continue;
                }

                await TryPurgeSensitiveMediaAsync(userId);

                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Your Vox Angelos Account Has Been Approved",
                    $"<div style='font-family:Arial,sans-serif; max-width:480px; margin:0 auto;'>" +
                    $"<h2 style='color:#1a237e;'>Account Approved!</h2>" +
                    $"<p>Hello,</p>" +
                    $"<p>Great news! Your <strong>Vox Angelos</strong> citizen account has been reviewed and <strong style='color:#2e7d32;'>approved</strong>.</p>" +
                    $"<p>You can now log in and start using the platform.</p>" +
                    $"<div style='text-align:center; margin:2rem 0;'>" +
                    $"<a href='https://localhost:7244/Identity/Account/Login' " +
                    $"style='background:#1a237e; color:#ffffff; padding:12px 28px; text-decoration:none; border-radius:6px; font-weight:bold; display:inline-block;'>" +
                    $"Log In to Vox Angelos</a>" +
                    $"</div>" +
                    $"<p style='color:#888; font-size:0.85rem;'>If you did not register for this account, please ignore this email.</p>" +
                    $"<p style='color:#888; font-size:0.85rem;'>— The Vox Angelos Team</p>" +
                    $"</div>");

                _logger.LogInformation("Admin bulk-approved citizen {UserId}", userId);
                approved++;
            }

            TempData["AdminSuccess"] = skipped == 0
                ? $"{approved} application(s) approved and notified successfully."
                : $"{approved} application(s) approved. {skipped} were skipped (already decided or not found).";

            return RedirectToPage(new { filterStatus, faceMatchFilter, search, sort });
        }

        public async Task<IActionResult> OnPostBulkRejectAsync(
            string[] userIds,
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string? search = null,
            string? sort = null)
        {
            var rejected = 0;
            var skipped = 0;

            foreach (var userId in userIds ?? Array.Empty<string>())
            {
                if (userId == ReviewApplicationModel.DevelopmentMockCitizenId)
                {
                    skipped++;
                    continue;
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null || user.ApprovalStatus != "Pending")
                {
                    skipped++;
                    continue;
                }

                user.ApprovalStatus = "Rejected";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    skipped++;
                    continue;
                }

                await TryPurgeSensitiveMediaAsync(userId);

                // Records when this application was rejected — the timestamp
                // RejectedApplicationPurgeService reads to permanently delete the
                // account 7 days later, per the Data Privacy Act retention policy.
                var accountApproval = await _context.AccountApprovals
                    .FirstOrDefaultAsync(a => a.UserId == userId);
                if (accountApproval == null)
                {
                    accountApproval = new AccountApproval { UserId = userId };
                    _context.AccountApprovals.Add(accountApproval);
                }
                accountApproval.Status = "Rejected";
                accountApproval.RejectionReason = null;
                accountApproval.ReviewedAt = DateTime.UtcNow;
                accountApproval.ReviewedByAdminId = _userManager.GetUserId(User);
                await _context.SaveChangesAsync();

                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Your Vox Angelos Account Application",
                    $"<div style='font-family:Arial,sans-serif; max-width:480px; margin:0 auto;'>" +
                    $"<h2 style='color:#c62828;'>Application Update</h2>" +
                    $"<p>Hello,</p>" +
                    $"<p>We regret to inform you that your Vox Angelos citizen account has been <strong style='color:#c62828;'>rejected</strong>.</p>" +
                    $"<p><strong>Reason:</strong> Does not meet verification requirements.</p>" +
                    $"<p>If you believe this is an error, please contact support.</p>" +
                    $"<p style='color:#888; font-size:0.85rem;'>— The Vox Angelos Team</p>" +
                    $"</div>");

                _logger.LogInformation("Admin bulk-rejected citizen {UserId}", userId);
                rejected++;
            }

            TempData["AdminSuccess"] = skipped == 0
                ? $"{rejected} application(s) rejected and notified successfully."
                : $"{rejected} application(s) rejected. {skipped} were skipped (already decided or not found).";

            return RedirectToPage(new { filterStatus, faceMatchFilter, search, sort });
        }

        public async Task<IActionResult> OnPostBulkDeleteAsync(
            string[] userIds,
            string filterStatus = "All",
            string faceMatchFilter = "All",
            string? search = null,
            string? sort = null)
        {
            var deleted = 0;
            var skipped = 0;

            foreach (var userId in userIds ?? Array.Empty<string>())
            {
                if (userId == ReviewApplicationModel.DevelopmentMockCitizenId)
                {
                    skipped++;
                    continue;
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    skipped++;
                    continue;
                }

                await TryPurgeSensitiveMediaAsync(userId);

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    _logger.LogWarning("Admin permanently deleted citizen application {UserId}", userId);
                    deleted++;
                }
                else
                {
                    skipped++;
                }
            }

            TempData["AdminSuccess"] = skipped == 0
                ? $"{deleted} application(s) permanently deleted."
                : $"{deleted} application(s) permanently deleted. {skipped} could not be deleted.";

            return RedirectToPage(new { filterStatus, faceMatchFilter, search, sort });
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
