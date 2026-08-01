using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class ReviewRecommendationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<FeedHub> _feedHub;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ReviewRecommendationsModel> _logger;

        public ReviewRecommendationsModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<FeedHub> feedHub,
            IWebHostEnvironment environment,
            IEmailSender emailSender,
            ILogger<ReviewRecommendationsModel> logger)
        {
            _db = db;
            _userManager = userManager;
            _feedHub = feedHub;
            _environment = environment;
            _emailSender = emailSender;
            _logger = logger;
        }

        public string[] Departments => ConcernClassificationService.Departments;

        public List<RecommendationViewModel> Recommendations { get; set; } = new();
        public string CurrentFilter { get; set; } = "Pending";

        public class RecommendationViewModel
        {
            public int Id { get; set; }
            public string CitizenName { get; set; } = string.Empty;
            public string Justification { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string? AssignedOffice { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Beneficiaries { get; set; } = string.Empty;
            public int EstimatedPeopleAffected { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? LguNotes { get; set; }
            public DateTime SubmittedAt { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public List<string> AttachmentPaths { get; set; } = new();
            public List<string> AttachmentTypes { get; set; } = new();
        }

        public async Task OnGetAsync(string filter = "Pending")
        {
            CurrentFilter = filter;

            var user = await _userManager.GetUserAsync(User);
            var userDepartment = user?.Department;

            var query = _db.Recommendations
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .Where(r => r.Status != "Draft")
                .AsQueryable();

            // Show recommendations whose classified office matches this LGU's department
            if (!string.IsNullOrEmpty(userDepartment))
            {
                query = query.Where(r => r.AssignedOffice == userDepartment || r.AssignedOffice == null);
            }

            if (filter != "All")
                query = query.Where(r => r.Status == filter);

            var recs = await query
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            Recommendations = recs.Select(r =>
            {
                var availableAttachments = r.Attachments
                    .OrderBy(a => a.UploadedAt)
                    .Select(a => new
                    {
                        Path = NormalizeAvailableAttachmentPath(a.FilePath),
                        a.FileType
                    })
                    .Where(a => a.Path != null)
                    .ToList();

                return new RecommendationViewModel
                {
                    Id = r.Id,
                    CitizenName = r.Citizen.UserProfile != null
                        ? $"{r.Citizen.UserProfile.FirstName} {r.Citizen.UserProfile.LastName}"
                        : r.Citizen.Email ?? "Citizen",
                    Justification = r.Justification,
                    Category = r.Category,
                    AssignedOffice = r.AssignedOffice,
                    Title = r.Title,
                    Location = r.Location,
                    Description = r.Description,
                    Beneficiaries = r.Beneficiaries,
                    EstimatedPeopleAffected = r.EstimatedPeopleAffected,
                    Status = r.Status,
                    LguNotes = r.LguNotes,
                    SubmittedAt = r.SubmittedAt,
                    ReviewedAt = r.ReviewedAt,
                    AttachmentPaths = availableAttachments.Select(a => a.Path!).ToList(),
                    AttachmentTypes = availableAttachments.Select(a => a.FileType).ToList()
                };
            }).ToList();
        }

        private string? NormalizeAvailableAttachmentPath(string? attachmentPath)
        {
            if (string.IsNullOrWhiteSpace(attachmentPath))
                return null;

            if (Uri.TryCreate(attachmentPath, UriKind.Absolute, out var remoteUri) &&
                (remoteUri.Scheme == Uri.UriSchemeHttps || remoteUri.Scheme == Uri.UriSchemeHttp))
            {
                return remoteUri.ToString();
            }

            var relativePath = attachmentPath
                .TrimStart('~', '/', '\\')
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var webRoot = Path.GetFullPath(_environment.WebRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var requiredPrefix = webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.File.Exists(fullPath))
            {
                return null;
            }

            return "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        private async Task SendCitizenEmailIfEnabledAsync(
            string citizenId,
            string subject,
            string heading,
            string message)
        {
            var recipient = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == citizenId &&
                               user.EmailNotificationsEnabled &&
                               user.EmailConfirmed &&
                               user.Email != null)
                .Select(user => new { user.Email })
                .SingleOrDefaultAsync();

            if (recipient?.Email == null)
                return;

            var recommendationsUrl = Url.Page("/User/Recommendations", null, null, Request.Scheme)
                ?? "/User/Recommendations";
            var htmlMessage =
                "<div style='font-family:Arial,sans-serif;max-width:520px;margin:0 auto;color:#17213a;'>" +
                $"<h2 style='color:#1746b1;'>{WebUtility.HtmlEncode(heading)}</h2>" +
                $"<p style='line-height:1.6;'>{WebUtility.HtmlEncode(message)}</p>" +
                "<p style='margin:28px 0;'>" +
                $"<a href='{WebUtility.HtmlEncode(recommendationsUrl)}' style='background:#1746b1;color:#fff;padding:11px 18px;text-decoration:none;border-radius:7px;font-weight:700;'>View your recommendation</a>" +
                "</p>" +
                "<p style='color:#718096;font-size:12px;'>You received this because email updates are enabled in your Vox Angelos notifications.</p>" +
                "</div>";

            try
            {
                await _emailSender.SendEmailAsync(recipient.Email, subject, htmlMessage);
            }
            catch (Exception exception)
            {
                // The in-app notification and LGU decision remain successful even when
                // the optional email provider or network is temporarily unavailable.
                _logger.LogWarning(
                    exception,
                    "Could not send the optional recommendation update email to citizen {CitizenId}.",
                    citizenId);
            }
        }

        public async Task<IActionResult> OnPostApproveAsync(int recommendationId, string? lguNotes)
        {
            var user = await _userManager.GetUserAsync(User);
            var reviewedAt = DateTime.UtcNow;

            // Guarded by current Status so two staff reviewing the same recommendation
            // at once can't both "win" — only the first review is applied.
            var updated = await _db.Recommendations
                .Where(r => r.Id == recommendationId && r.Status == "Pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "Published")
                    .SetProperty(r => r.LguNotes, lguNotes)
                    .SetProperty(r => r.ReviewedByLguId, user!.Id)
                    .SetProperty(r => r.ReviewedAt, reviewedAt));

            if (updated == 0)
                TempData["RecError"] = "This recommendation was already reviewed by another staff member.";
            else
            {
                var recommendation = await _db.Recommendations
                    .Where(r => r.Id == recommendationId)
                    .Select(r => new { r.CitizenId })
                    .SingleAsync();
                var actorName = user?.Department ?? user?.Email ?? "LGU Office";
                var notificationMessage = string.IsNullOrWhiteSpace(lguNotes)
                    ? "Your recommendation has been published."
                    : lguNotes;

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recommendation.CitizenId,
                    Title = "Your recommendation was published",
                    Message = notificationMessage,
                    NotificationType = "RecommendationUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Recommendations",
                    CreatedAt = reviewedAt
                });
                await _db.SaveChangesAsync();
                await SendCitizenEmailIfEnabledAsync(
                    recommendation.CitizenId,
                    "Your Vox Angelos recommendation was published",
                    "Your recommendation was published",
                    notificationMessage);

                await _feedHub.Clients.Group(FeedHub.DiscoverGroup).SendAsync("PostPublished");
                TempData["RecSuccess"] = "The recommendation was approved and published successfully.";
            }

            return RedirectToPage(new { filter = "Pending" });
        }

        public async Task<IActionResult> OnPostRejectAsync(int recommendationId, string? lguNotes)
        {
            var user = await _userManager.GetUserAsync(User);
            var reviewedAt = DateTime.UtcNow;

            var updated = await _db.Recommendations
                .Where(r => r.Id == recommendationId && r.Status == "Pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "Rejected")
                    .SetProperty(r => r.LguNotes, lguNotes)
                    .SetProperty(r => r.ReviewedByLguId, user!.Id)
                    .SetProperty(r => r.ReviewedAt, reviewedAt));

            if (updated == 0)
                TempData["RecError"] = "This recommendation was already reviewed by another staff member.";
            else
            {
                var recommendation = await _db.Recommendations
                    .Where(r => r.Id == recommendationId)
                    .Select(r => new { r.CitizenId })
                    .SingleAsync();
                var actorName = user?.Department ?? user?.Email ?? "LGU Office";
                var notificationMessage = string.IsNullOrWhiteSpace(lguNotes)
                    ? "Your recommendation was not published."
                    : lguNotes;

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recommendation.CitizenId,
                    Title = "Your recommendation was not published",
                    Message = notificationMessage,
                    NotificationType = "RecommendationUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Recommendations",
                    CreatedAt = reviewedAt
                });
                await _db.SaveChangesAsync();
                await SendCitizenEmailIfEnabledAsync(
                    recommendation.CitizenId,
                    "Update on your Vox Angelos recommendation",
                    "Your recommendation was not published",
                    notificationMessage);
                TempData["RecSuccess"] = "The recommendation was rejected successfully. The citizen was notified.";
            }

            return RedirectToPage(new { filter = "Pending" });
        }

        private enum ReassignOutcome { Success, NotFound, Forbidden, NotEligible, AlreadyReviewed }

        // Shared by the single-recommendation reassign handler and the bulk one below, so
        // both go through the exact same eligibility check and update — a bulk reassign is
        // just this run in a loop, not a separate code path.
        private async Task<ReassignOutcome> TryReassignRecommendationAsync(int recommendationId, string newOffice, ApplicationUser user)
        {
            var recommendation = await _db.Recommendations
                .Where(r => r.Id == recommendationId)
                .Select(r => new { r.AssignedOffice, r.Status })
                .FirstOrDefaultAsync();
            if (recommendation == null) return ReassignOutcome.NotFound;

            // Unassigned recommendations (AssignedOffice == null) are open to any LGU
            // office to claim and route — only block hijacking one another office already owns.
            if (recommendation.AssignedOffice != null && user.Department != recommendation.AssignedOffice)
                return ReassignOutcome.Forbidden;

            if (recommendation.Status != "Pending") return ReassignOutcome.NotEligible;

            var updated = await _db.Recommendations
                .Where(r => r.Id == recommendationId && r.Status == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AssignedOffice, newOffice));

            return updated == 0 ? ReassignOutcome.AlreadyReviewed : ReassignOutcome.Success;
        }

        // Lets an LGU office correct a recommendation the NLP classifier routed to the
        // wrong office, or claim an unassigned one — same pattern as the concern-side
        // reassign feature in Pages/LGU/Index.cshtml.cs.
        public async Task<IActionResult> OnPostReassignOfficeAsync(int recommendationId, string newOffice)
        {
            if (!ConcernClassificationService.Departments.Contains(newOffice))
                return BadRequest("Unknown department.");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            var outcome = await TryReassignRecommendationAsync(recommendationId, newOffice, user);

            switch (outcome)
            {
                case ReassignOutcome.NotFound:
                    return NotFound();
                case ReassignOutcome.Forbidden:
                    return Forbid();
                case ReassignOutcome.NotEligible:
                    TempData["RecError"] = "Only pending recommendations can be reassigned.";
                    break;
                case ReassignOutcome.AlreadyReviewed:
                    TempData["RecError"] = "This recommendation was already reviewed by another staff member.";
                    break;
                case ReassignOutcome.Success:
                    TempData["RecSuccess"] = $"The recommendation was reassigned to {newOffice}.";
                    break;
            }

            return RedirectToPage(new { filter = "Pending" });
        }

        // Bulk version of the reassign feature above — lets an LGU staffer route many
        // unassigned/misrouted recommendations to the correct office in one submit instead
        // of clicking through the single-item modal for each one.
        public async Task<IActionResult> OnPostBulkReassignOfficeAsync(int[] recommendationIds, string newOffice)
        {
            if (!ConcernClassificationService.Departments.Contains(newOffice))
                return BadRequest("Unknown department.");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            if (recommendationIds == null || recommendationIds.Length == 0)
            {
                TempData["RecError"] = "No recommendations were selected.";
                return RedirectToPage(new { filter = "Pending" });
            }

            int succeeded = 0, skipped = 0;

            foreach (var recommendationId in recommendationIds.Distinct())
            {
                var previousOffice = await _db.Recommendations
                    .Where(r => r.Id == recommendationId)
                    .Select(r => r.AssignedOffice)
                    .FirstOrDefaultAsync();

                // Safeguard: a "select all" can sweep up recommendations already assigned to
                // your own office alongside genuinely unassigned ones. Bulk reassign only
                // touches the unassigned ones — moving an already-correctly-routed item needs
                // a deliberate single-item "Reassign office" correction, not a blanket action.
                if (previousOffice != null && previousOffice == user.Department)
                {
                    skipped++;
                    continue;
                }

                var outcome = await TryReassignRecommendationAsync(recommendationId, newOffice, user);
                if (outcome == ReassignOutcome.Success) succeeded++;
                else skipped++;
            }

            TempData["RecSuccess"] = skipped == 0
                ? $"Reassigned {succeeded} recommendation(s) to {newOffice}."
                : $"Reassigned {succeeded} recommendation(s) to {newOffice}. {skipped} were skipped (already assigned to your office, already reviewed, or owned by another office).";

            return RedirectToPage(new { filter = "Pending" });
        }
    }
}
