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
        private readonly IConfiguration _configuration; 
        private readonly ILogger<ReviewRecommendationsModel> _logger;

        public ReviewRecommendationsModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<FeedHub> feedHub,
            IWebHostEnvironment environment,
            IEmailSender emailSender,
            ILogger<ReviewRecommendationsModel> logger,
            IConfiguration configuration)
        {
            _db = db;
            _userManager = userManager;
            _feedHub = feedHub;
            _environment = environment;
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
            _configuration = configuration;
        }

        public string[] Departments => ConcernClassificationService.Departments;

        public List<RecommendationViewModel> Recommendations { get; set; } = new();
        public string CurrentFilter { get; set; } = "Pending";
        public string SearchTerm { get; set; } = string.Empty;
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string SortOrder { get; set; } = "newest";

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

        public async Task OnGetAsync(
            string filter = "Pending",
            string? search = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? sort = null)
        {
            CurrentFilter = filter;
            SearchTerm = search?.Trim() ?? string.Empty;
            DateFrom = dateFrom?.Date;
            DateTo = dateTo?.Date;
            SortOrder = string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase)
                ? "oldest"
                : "newest";

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
                query = _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized")
                    ? query.Where(r => r.AssignedOffice == userDepartment)
                    : query.Where(r => r.AssignedOffice == userDepartment || r.AssignedOffice == null);
            }

            if (filter != "All")
                query = query.Where(r => r.Status == filter);

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var searchPattern = $"%{SearchTerm}%";
                query = query.Where(r =>
                    EF.Functions.ILike(r.Title, searchPattern) ||
                    EF.Functions.ILike(r.Description, searchPattern) ||
                    EF.Functions.ILike(r.Justification, searchPattern) ||
                    EF.Functions.ILike(r.Location, searchPattern) ||
                    EF.Functions.ILike(r.Beneficiaries, searchPattern));
            }

            if (DateFrom.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(DateFrom.Value, DateTimeKind.Utc);
                query = query.Where(r => r.SubmittedAt >= fromUtc);
            }

            if (DateTo.HasValue)
            {
                var untilUtc = DateTime.SpecifyKind(DateTo.Value.AddDays(1), DateTimeKind.Utc);
                query = query.Where(r => r.SubmittedAt < untilUtc);
            }

            query = SortOrder == "oldest"
                ? query.OrderBy(r => r.SubmittedAt)
                : query.OrderByDescending(r => r.SubmittedAt);

            var recs = await query.ToListAsync();

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

            var publicBaseUrl = _configuration["App:PublicBaseUrl"] ?? "https://voxangelos.onrender.com";
            var htmlMessage = BuildRecommendationEmail(heading, message, publicBaseUrl);

            try
            {
                await _emailSender.SendEmailAsync(recipient.Email, subject, htmlMessage);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not send the optional recommendation update email to citizen {CitizenId}.",
                    citizenId);
            }
        }

        private static string BuildRecommendationEmail(string heading, string message, string publicBaseUrl)
        {
            string baseUrl = publicBaseUrl?.TrimEnd('/') ?? "";
            var safeHeading = WebUtility.HtmlEncode(heading);
            var safeMessage = WebUtility.HtmlEncode(message);
            var recommendationsUrl = "/User/Recommendations";

            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>{safeHeading}</title>
</head>
<body style=""margin:0; padding:0; background-color:#eaecf5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#eaecf5; padding:32px 16px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:480px; background-color:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 8px 24px rgba(22,70,160,0.12);"">

          <!-- Header with Logo -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2b45b0 0%,#1a2a6c 100%); background-color:#1646a0; padding:32px 40px; text-align:center;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 auto;"">
                <tr>
                  <td style=""vertical-align:middle;"">
                    <img src=""{baseUrl}/assets/VoxAngelosLogo.png"" alt=""Vox Angelos"" style=""height:40px; display:block; border:0px;"" />
                  </td>
                  <td style=""padding-left:12px; color:#ffffff; font-size:18px; font-weight:700; letter-spacing:-0.01em;"">
                    Vox Angelos
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:40px;"">
              <p style=""margin:0 0 8px; color:#1646a0; font-size:11px; font-weight:800; letter-spacing:0.1em; text-transform:uppercase;"">
                Recommendation Update
              </p>
              <h1 style=""margin:0 0 16px; color:#172033; font-size:24px; font-weight:800; letter-spacing:-0.02em; line-height:1.25;"">
                {safeHeading}
              </h1>
              <p style=""margin:0 0 28px; color:#5c687b; font-size:14px; line-height:1.6;"">
                {safeMessage}
              </p>

              <!-- Button -->
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""border-radius:8px; background-color:#1646a0;"">
                    <a href=""{baseUrl}{recommendationsUrl}""
                       style=""display:inline-block; padding:14px 28px; color:#ffffff; font-size:13px; font-weight:700; letter-spacing:0.02em; text-transform:uppercase; text-decoration:none; border-radius:8px;"">
                      View Your Recommendation
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:24px 40px; border-top:1px solid #eef1f6; background-color:#fafbfc;"">
              <p style=""margin:0; color:#a3adbd; font-size:11px; line-height:1.6;"">
                Protected by <strong style=""color:#5b6577;"">RA 10173</strong>. Your data is handled in accordance with the Data Privacy Act of the Philippines.
              </p>
            </td>
          </tr>

        </table>

        <p style=""margin:20px 0 0; color:#a3adbd; font-size:11px;"">
          &copy; 2026 Vox Angelos
        </p>
      </td>
    </tr>
  </table>
</body>
</html>";
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
            if (recommendation.AssignedOffice == null &&
                _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized"))
                return ReassignOutcome.Forbidden;
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
