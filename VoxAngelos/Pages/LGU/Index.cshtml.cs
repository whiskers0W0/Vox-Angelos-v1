using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Configuration;
using System.Net;
using System.Text.Json;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ConcernClassificationService _classifier;
        private readonly IHubContext<FeedHub> _feedHub;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<IndexModel> _logger;
        private static readonly IReadOnlyDictionary<string, string> DepartmentDisplayNames =
            new Dictionary<string, string>
            {
                ["SWDO"] = "Social Welfare and Development Office",
                ["CEO"] = "City Engineer's Office",
                ["CENRO"] = "City Environment and Natural Resources Office",
                ["ACDO"] = "City Development / Urban Planning Office",
                ["PTRO"] = "Public Safety, Traffic and Transport Regulation Office",
                ["OSCA"] = "Office of Senior Citizens Affairs",
                ["PWDAO"] = "Persons With Disability Affairs Office"
            };

        public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            ConcernClassificationService classifier, IHubContext<FeedHub> feedHub,
            IWebHostEnvironment environment, IEmailSender emailSender,
            ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _db = db;
            _userManager = userManager;
            _classifier = classifier;
            _feedHub = feedHub;
            _environment = environment;
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
        }

        // Notifies every LGU dashboard that could be displaying this concern — its
        // current department plus whatever it used to be, in case a reassign just moved
        // it out of one department's view and into another's.
        private Task NotifyDepartmentsAsync(params string?[] departments)
        {
            var tasks = departments
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .Select(d => _feedHub.Clients.Group(FeedHub.LguDepartmentGroup(d!)).SendAsync("ConcernFeedChanged"));
            return Task.WhenAll(tasks);
        }

        private static string GetDepartmentDisplayName(string department) =>
            DepartmentDisplayNames.GetValueOrDefault(department, department);

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

            // Get public base url from configuration (fallback to a default if not found)
            var publicBaseUrl = _configuration?["App:PublicBaseUrl"] ?? "https://voxangelos.onrender.com";

            var htmlMessage = BuildConcernEmail(heading, message, publicBaseUrl);

            try
            {
                await _emailSender.SendEmailAsync(recipient.Email, subject, htmlMessage);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not send the optional concern update email to citizen {CitizenId}.",
                    citizenId);
            }
        }

        private static string BuildConcernEmail(string heading, string message, string publicBaseUrl)
        {
            string baseUrl = publicBaseUrl?.TrimEnd('/') ?? "";
            var safeHeading = WebUtility.HtmlEncode(heading);
            var safeMessage = WebUtility.HtmlEncode(message);
            var updatesUrl = "/User/Concerns";

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
                Concern Update
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
                    <a href=""{baseUrl}{updatesUrl}""
                       style=""display:inline-block; padding:14px 28px; color:#ffffff; font-size:13px; font-weight:700; letter-spacing:0.02em; text-transform:uppercase; text-decoration:none; border-radius:8px;"">
                      View Your Concern
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
                Protected by <strong style=""color:#5b6577;"">RA 10173</strong>. Your recovery data is handled in accordance with the Data Privacy Act of the Philippines.
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

        public string[] Departments => ConcernClassificationService.Departments;

        public List<ConcernViewModel> Concerns { get; set; } = new();
        public string CurrentFilter { get; set; } = "Unresolved";
        public string? CurrentUserDepartment { get; set; }

        public async Task<IActionResult> OnGetAsync(string? filter, int? concernId)
        {
            CurrentFilter = filter ?? "Unresolved";

            var user = await _userManager.GetUserAsync(User);
            var userDepartment = user?.Department;
            CurrentUserDepartment = userDepartment;

            if (concernId.HasValue)
            {
                var canViewConcern = !string.IsNullOrWhiteSpace(userDepartment) &&
                    await _db.Concerns.AnyAsync(concern =>
                        concern.Id == concernId.Value && concern.Category == userDepartment);
                if (!canViewConcern)
                    return Forbid();
            }

            var query = _db.Concerns
                .Include(c => c.Attachments)
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .Where(c => c.Status != "Draft")
                .AsQueryable();

            // Show concerns whose classified category matches this LGU's department
            if (!string.IsNullOrEmpty(userDepartment))
            {
                query = _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized")
                    ? query.Where(c => c.Category == userDepartment)
                    : query.Where(c => c.Category == userDepartment || c.Category == null);
            }

            if (CurrentFilter != "All")
            {
                query = query.Where(c => c.Status == CurrentFilter);
            }

            var reviewedConcernIds = (await _db.ClassificationCorrections
                .Select(cc => cc.ConcernId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            Concerns = await query
                .OrderByDescending(c => c.SubmittedAt)
                .Select(c => new ConcernViewModel
                {
                    Id = c.Id,
                    CitizenName = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName} {c.Citizen.UserProfile.LastName}"
                        : c.Citizen.Email,
                    Initials = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName[0]}{c.Citizen.UserProfile.LastName[0]}"
                        : "??",
                    Description = c.Description,
                    Category = c.Category ?? "Uncategorized",
                    RawCategory = c.Category,
                    Status = c.Status,
                    LocationName = c.LocationName ?? "No location provided",
                    Latitude = c.Latitude,
                    Longitude = c.Longitude,
                    LocationDensityScore = c.LocationDensityScore,
                    SubmittedAt = c.SubmittedAt,
                    FirstAttachmentPath = c.Attachments
                        .Where(a => a.FileType == "image")
                        .Select(a => a.FilePath)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var barangayBoundaries = await LoadBarangayBoundariesAsync();

            foreach (var concern in Concerns)
            {
                concern.HasFeedback = reviewedConcernIds.Contains(concern.Id);
                concern.FirstAttachmentPath = GetAvailableAttachmentPath(concern.FirstAttachmentPath);

                if (concern.Latitude.HasValue && concern.Longitude.HasValue)
                {
                    concern.BarangayName = FindBarangay(
                        concern.Latitude.Value,
                        concern.Longitude.Value,
                        barangayBoundaries);
                }
            }

            return Page();
        }

        private sealed class BarangayBoundary
        {
            public string Name { get; init; } = string.Empty;
            public string GeometryType { get; init; } = string.Empty;
            public JsonElement Coordinates { get; init; }
        }

        private async Task<List<BarangayBoundary>> LoadBarangayBoundariesAsync()
        {
            var geoJsonPath = Path.Combine(
                _environment.WebRootPath,
                "geojson",
                "angeles-city-barangays.geojson");

            if (!System.IO.File.Exists(geoJsonPath))
                return new List<BarangayBoundary>();

            await using var stream = System.IO.File.OpenRead(geoJsonPath);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.GetProperty("features")
                .EnumerateArray()
                .Select(feature => new BarangayBoundary
                {
                    Name = feature.GetProperty("properties").GetProperty("name").GetString() ?? string.Empty,
                    GeometryType = feature.GetProperty("geometry").GetProperty("type").GetString() ?? string.Empty,
                    Coordinates = feature.GetProperty("geometry").GetProperty("coordinates").Clone()
                })
                .ToList();
        }

        private static string? FindBarangay(
            double latitude,
            double longitude,
            IEnumerable<BarangayBoundary> boundaries)
        {
            foreach (var boundary in boundaries)
            {
                var containsPoint = boundary.GeometryType switch
                {
                    "Polygon" => IsPointInPolygon(longitude, latitude, boundary.Coordinates),
                    "MultiPolygon" => boundary.Coordinates.EnumerateArray()
                        .Any(polygon => IsPointInPolygon(longitude, latitude, polygon)),
                    _ => false
                };

                if (containsPoint)
                    return boundary.Name;
            }

            return null;
        }

        private static bool IsPointInPolygon(double longitude, double latitude, JsonElement polygon)
        {
            if (polygon.GetArrayLength() == 0 || !IsPointInRing(longitude, latitude, polygon[0]))
                return false;

            return !polygon.EnumerateArray().Skip(1)
                .Any(hole => IsPointInRing(longitude, latitude, hole));
        }

        private static bool IsPointInRing(double longitude, double latitude, JsonElement ring)
        {
            var inside = false;
            var pointCount = ring.GetArrayLength();
            if (pointCount < 3)
                return false;

            for (int current = 0, previous = pointCount - 1;
                 current < pointCount;
                 previous = current++)
            {
                var currentLongitude = ring[current][0].GetDouble();
                var currentLatitude = ring[current][1].GetDouble();
                var previousLongitude = ring[previous][0].GetDouble();
                var previousLatitude = ring[previous][1].GetDouble();

                var crossesLatitude = (currentLatitude > latitude) != (previousLatitude > latitude);
                if (crossesLatitude && longitude <
                    (previousLongitude - currentLongitude) * (latitude - currentLatitude) /
                    (previousLatitude - currentLatitude) + currentLongitude)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private string? GetAvailableAttachmentPath(string? attachmentPath)
        {
            if (string.IsNullOrWhiteSpace(attachmentPath))
                return null;

            if (Uri.TryCreate(attachmentPath, UriKind.Absolute, out var remoteUri) &&
                (remoteUri.Scheme == Uri.UriSchemeHttps || remoteUri.Scheme == Uri.UriSchemeHttp))
            {
                return attachmentPath;
            }

            var relativePath = attachmentPath
                .TrimStart('~', '/', '\\')
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var webRoot = Path.GetFullPath(_environment.WebRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var requiredPrefix = webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return System.IO.File.Exists(fullPath) ? attachmentPath : null;
        }

        // Shared by the single-concern confirm handler and the bulk one below.
        private async Task<bool> TryConfirmConcernCategoryAsync(int concernId, ApplicationUser user)
        {
            var concern = await _db.Concerns.FindAsync(concernId);
            if (concern == null || string.IsNullOrEmpty(concern.Category)) return false;
            if (concern.Category != user.Department) return false;

            try
            {
                await _classifier.RecordCorrectionAsync(concernId, concern.Category, wasCorrect: true, user.Id);
                return true;
            }
            catch (ConcernAlreadyReviewedException)
            {
                return false;
            }
        }

        // Shared by both bulk handlers: has a staffer already recorded a correct/incorrect
        // verdict on this concern's classification? Once reviewed, a concern is considered
        // settled and bulk actions (which can sweep up a mixed "select all") should leave
        // it alone — only a deliberate single-item action should touch it again.
        private async Task<bool> IsAlreadyReviewedAsync(int concernId) =>
            await _db.ClassificationCorrections.AnyAsync(cc => cc.ConcernId == concernId);

        public async Task<IActionResult> OnPostConfirmCategoryAsync(int concernId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            if (!await TryConfirmConcernCategoryAsync(concernId, user))
                TempData["ConcernError"] = "This concern could not be confirmed — it may already be reviewed by another staff member.";
            else
                TempData["ConcernSuccess"] = "The concern category was confirmed successfully.";

            return RedirectToPage(new { filter = CurrentFilter });
        }

        // Bulk version — confirms the NLP-assigned category is correct for many
        // concerns at once instead of clicking "Category correct" on each card.
        // A mixed selection can include three kinds of concern:
        //  - already reviewed (confirmed correct or previously reassigned) -> skipped, settled
        //  - auto-classified but not yet reviewed -> confirmed correct (existing behavior)
        //  - never auto-classified (no category) -> "Correct" here means "this is mine",
        //    so it's claimed for the caller's own department instead of being skipped
        public async Task<IActionResult> OnPostBulkConfirmCategoryAsync(int[] concernIds, string? filter)
        {
            CurrentFilter = filter ?? "Unresolved";

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            if (concernIds == null || concernIds.Length == 0)
            {
                TempData["ConcernError"] = "No concerns were selected.";
                return RedirectToPage(new { filter = CurrentFilter });
            }

            if (string.IsNullOrEmpty(user.Department))
            {
                TempData["ConcernError"] = "Your account has no department assigned.";
                return RedirectToPage(new { filter = CurrentFilter });
            }

            int confirmed = 0, claimed = 0, skipped = 0;

            foreach (var concernId in concernIds.Distinct())
            {
                if (await IsAlreadyReviewedAsync(concernId))
                {
                    skipped++;
                    continue;
                }

                var category = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => c.Category)
                    .FirstOrDefaultAsync();

                if (category == null)
                {
                    var outcome = await TryReassignConcernAsync(concernId, user.Department, user);
                    if (outcome == ReassignOutcome.Success) claimed++;
                    else skipped++;
                }
                else if (await TryConfirmConcernCategoryAsync(concernId, user))
                {
                    confirmed++;
                }
                else
                {
                    skipped++;
                }
            }

            if (claimed > 0) await NotifyDepartmentsAsync(user.Department);

            var parts = new List<string>();
            if (confirmed > 0) parts.Add($"confirmed {confirmed} concern(s) as correctly classified");
            if (claimed > 0) parts.Add($"claimed {claimed} unclassified concern(s) for {GetDepartmentDisplayName(user.Department)}");
            var message = parts.Count > 0 ? string.Join(" and ", parts) : "No concerns were updated";
            message = char.ToUpper(message[0]) + message[1..];

            TempData["ConcernSuccess"] = skipped == 0
                ? $"{message}."
                : $"{message}. {skipped} were skipped (already reviewed).";

            return RedirectToPage(new { filter = CurrentFilter });
        }

        private enum ReassignOutcome { Success, NotFound, Forbidden, NotEligible, AlreadyReviewed }

        // Shared by the single-concern reassign handler and the bulk one below, so both
        // go through the exact same audit-trail write (ClassificationCorrection + learned
        // weights via RecordCorrectionAsync), citizen notification, and timeline event —
        // a bulk reassign is just this run in a loop, not a separate code path.
        private async Task<ReassignOutcome> TryReassignConcernAsync(int concernId, string newCategory, ApplicationUser user)
        {
            var concern = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => new { c.Category, c.CitizenId, c.Status })
                .FirstOrDefaultAsync();
            if (concern == null) return ReassignOutcome.NotFound;

            // Uncategorized concerns (Category == null) are open to any LGU office to
            // claim and route — only block hijacking a concern another office already owns.
            if (concern.Category == null &&
                _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized"))
                return ReassignOutcome.Forbidden;
            if (concern.Category != null && user.Department != concern.Category) return ReassignOutcome.Forbidden;

            if (concern.Status != "Unresolved") return ReassignOutcome.NotEligible;

            try
            {
                await _classifier.RecordCorrectionAsync(concernId, newCategory, wasCorrect: false, user.Id);

                var forwardedAt = DateTime.UtcNow;
                var actorName = user.Department ?? user.Email ?? "LGU Office";
                var updateMessage = $"Your concern was forwarded to the {GetDepartmentDisplayName(newCategory)} for review.";

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Concern Forwarded",
                    Status = "Unresolved",
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = forwardedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was forwarded",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Concerns",
                    CreatedAt = forwardedAt
                });

                await _db.SaveChangesAsync();
                await SendCitizenEmailIfEnabledAsync(
                    concern.CitizenId,
                    "Your Vox Angelos concern was forwarded",
                    "Your concern was forwarded",
                    updateMessage);
                return ReassignOutcome.Success;
            }
            catch (ConcernAlreadyReviewedException)
            {
                return ReassignOutcome.AlreadyReviewed;
            }
        }

        // Manual Override Feature: lets an LGU admin correct a concern that the Google
        // NLP classifier (or the local keyword fallback) routed to the wrong department,
        // re-routing it to the correct one. See docs/manual-override-feature.md for the
        // full write-up (why it exists, how the audit trail works, how it feeds the NLP
        // feedback loop in ConcernClassificationService.RecordCorrectionAsync).
        public async Task<IActionResult> OnPostReassignCategoryAsync(int concernId, string newCategory)
        {
            if (!ConcernClassificationService.Departments.Contains(newCategory))
                return BadRequest("Unknown department.");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            var previousCategory = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => c.Category)
                .FirstOrDefaultAsync();

            var outcome = await TryReassignConcernAsync(concernId, newCategory, user);

            switch (outcome)
            {
                case ReassignOutcome.NotFound:
                    return NotFound();
                case ReassignOutcome.Forbidden:
                    return Forbid();
                case ReassignOutcome.NotEligible:
                    TempData["ConcernError"] = "Only unresolved concerns can be reassigned. Once an office accepts a concern, it must finish or escalate it through the LGU workflow.";
                    break;
                case ReassignOutcome.AlreadyReviewed:
                    TempData["ConcernError"] = "This concern was already reviewed by another staff member.";
                    break;
                case ReassignOutcome.Success:
                    await NotifyDepartmentsAsync(previousCategory, newCategory);
                    TempData["ConcernSuccess"] = $"The concern was reassigned to {GetDepartmentDisplayName(newCategory)}.";
                    break;
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        // Bulk version of the reassign feature above — lets an LGU staffer route many
        // uncategorized/misrouted concerns to the correct department in one submit
        // instead of clicking through the single-item modal for each one.
        public async Task<IActionResult> OnPostBulkReassignCategoryAsync(int[] concernIds, string newCategory, string? filter)
        {
            CurrentFilter = filter ?? "Unresolved";

            if (!ConcernClassificationService.Departments.Contains(newCategory))
                return BadRequest("Unknown department.");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            if (concernIds == null || concernIds.Length == 0)
            {
                TempData["ConcernError"] = "No concerns were selected.";
                return RedirectToPage(new { filter = CurrentFilter });
            }

            var touchedCategories = new HashSet<string?> { newCategory };
            int succeeded = 0, skipped = 0;

            foreach (var concernId in concernIds.Distinct())
            {
                // Safeguard: a "select all" can sweep up concerns that are already settled
                // (confirmed correct, or previously reassigned) alongside ones still needing
                // a decision. Bulk "Wrong" only touches not-yet-reviewed concerns — auto-
                // classified-but-unconfirmed and never-classified ones both qualify; an
                // already-reviewed one needs a deliberate single-item correction instead.
                if (await IsAlreadyReviewedAsync(concernId))
                {
                    skipped++;
                    continue;
                }

                var previousCategory = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => c.Category)
                    .FirstOrDefaultAsync();

                var outcome = await TryReassignConcernAsync(concernId, newCategory, user);
                if (outcome == ReassignOutcome.Success)
                {
                    succeeded++;
                    touchedCategories.Add(previousCategory);
                }
                else
                {
                    skipped++;
                }
            }

            await NotifyDepartmentsAsync(touchedCategories.ToArray());

            TempData["ConcernSuccess"] = skipped == 0
                ? $"Reassigned {succeeded} concern(s) to {GetDepartmentDisplayName(newCategory)}."
                : $"Reassigned {succeeded} concern(s) to {GetDepartmentDisplayName(newCategory)}. {skipped} were skipped (already reviewed, already resolved, or owned by another office).";

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(
            int concernId, string status, string? notes)
        {
            var lguUser = await _userManager.GetUserAsync(User);
            if (lguUser == null || string.IsNullOrWhiteSpace(lguUser.Department))
                return Forbid();

            var updatedAt = DateTime.UtcNow;

            // Guarded by current Status so two staff updating the same concern at once
            // can't overwrite each other — only the first update lands, atomically.
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId &&
                    c.Category == lguUser.Department &&
                    c.Status != "Resolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.LguNotes, notes)
                    .SetProperty(c => c.UpdatedAt, updatedAt));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern could not be updated — it may already be resolved or no longer exist.";
            }
            else
            {
                var concern = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => new { c.Category, c.CitizenId })
                    .SingleAsync();
                var actorName = lguUser?.Department ?? lguUser?.Email ?? "LGU Office";
                var updateMessage = string.IsNullOrWhiteSpace(notes)
                    ? $"The LGU updated the concern status to {status}."
                    : notes;

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Status Updated",
                    Status = status,
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = updatedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was updated",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Concerns",
                    CreatedAt = updatedAt
                });
                await _db.SaveChangesAsync();
                await SendCitizenEmailIfEnabledAsync(
                    concern.CitizenId,
                    $"Vox Angelos concern status: {status}",
                    "Your concern was updated",
                    updateMessage);

                await NotifyDepartmentsAsync(concern.Category);
                TempData["ConcernSuccess"] = $"The concern status was updated to {status}.";
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostChooseConcernAsync(int concernId)
        {
            var lguUser = await _userManager.GetUserAsync(User);
            if (lguUser == null || string.IsNullOrWhiteSpace(lguUser.Department))
                return Forbid();

            var updatedAt = DateTime.UtcNow;
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId &&
                    c.Category == lguUser.Department &&
                    c.Status == "Unresolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, "Chosen")
                    .SetProperty(c => c.UpdatedAt, updatedAt));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern was already claimed by another staff member.";
            }
            else
            {
                var concern = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => new { c.Category, c.CitizenId })
                    .SingleAsync();
                var actorName = lguUser?.Department ?? lguUser?.Email ?? "LGU Office";
                const string updateMessage = "An LGU office has accepted this concern for action.";

                var notifiedCategory = concern.Category;

                // Accepting a concern is itself a statement that the routing is right — fold
                // the classification-feedback step into Accept instead of requiring a
                // separate "Category correct" click first. Uncategorized concerns get
                // claimed for the accepting office; already-classified ones get auto-
                // confirmed as correct (a no-op if already reviewed).
                if (lguUser != null && !string.IsNullOrEmpty(lguUser.Department))
                {
                    var department = lguUser.Department;
                    if (concern.Category == null)
                    {
                        try
                        {
                            await _classifier.RecordCorrectionAsync(concernId, department, wasCorrect: false, lguUser.Id);
                            notifiedCategory = department;
                        }
                        catch (ConcernAlreadyReviewedException) { }
                    }
                    else if (concern.Category == department)
                    {
                        await TryConfirmConcernCategoryAsync(concernId, lguUser);
                    }
                }

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Concern Chosen",
                    Status = "Chosen",
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = updatedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was accepted",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Concerns",
                    CreatedAt = updatedAt
                });
                await _db.SaveChangesAsync();
                await SendCitizenEmailIfEnabledAsync(
                    concern.CitizenId,
                    "Your Vox Angelos concern was accepted",
                    "Your concern was accepted",
                    updateMessage);

                await NotifyDepartmentsAsync(notifiedCategory);
                TempData["ConcernSuccess"] = "The concern was accepted successfully. The citizen was notified.";
            }

            return RedirectToPage(new { filter = "Chosen" });
        }

        public async Task<IActionResult> OnPostCloseInvalidAsync(int concernId, string? reason)
        {
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10 || reason.Length > 500)
            {
                TempData["ConcernError"] = "Provide a clear reason between 10 and 500 characters before closing the concern.";
                return RedirectToPage(new { filter = "Unresolved" });
            }

            var lguUser = await _userManager.GetUserAsync(User);
            if (lguUser == null || string.IsNullOrWhiteSpace(lguUser.Department))
                return Forbid();

            var concern = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => new { c.Category, c.CitizenId, c.Status })
                .FirstOrDefaultAsync();

            if (concern == null)
                return NotFound();

            if (concern.Category == null)
            {
                TempData["ConcernError"] = "Assign this concern to the correct department before closing it.";
                return RedirectToPage(new { filter = "Unresolved" });
            }

            if (concern.Category != lguUser.Department)
                return Forbid();

            var closedAt = DateTime.UtcNow;
            var actorName = lguUser.Department ?? lguUser.Email ?? "LGU Office";
            var citizenMessage = $"The LGU closed this concern as invalid or non-actionable. Reason: {reason}";

            var executionStrategy = _db.Database.CreateExecutionStrategy();
            var concernClosed = false;

            await executionStrategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync();

                var updated = await _db.Concerns
                    .Where(c => c.Id == concernId &&
                                c.Status == "Unresolved" &&
                                c.Category == lguUser.Department)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Status, "Closed")
                        .SetProperty(c => c.LguNotes, reason)
                        .SetProperty(c => c.UpdatedAt, closedAt));

                if (updated == 0)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Concern Closed",
                    Status = "Closed",
                    Message = citizenMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = closedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was closed after review",
                    Message = citizenMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Concerns",
                    CreatedAt = closedAt
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                concernClosed = true;
            });

            if (!concernClosed)
            {
                TempData["ConcernError"] = "This concern was already accepted, closed, or updated by another staff member.";
                return RedirectToPage(new { filter = "Unresolved" });
            }

            await SendCitizenEmailIfEnabledAsync(
                concern.CitizenId,
                "Your Vox Angelos concern was closed after review",
                "Your concern was closed after review",
                citizenMessage);
            await NotifyDepartmentsAsync(concern.Category);

            TempData["ConcernSuccess"] = "The concern was closed and the citizen was notified.";
            return RedirectToPage(new { filter = "Unresolved" });
        }
    }

    public class ConcernViewModel
    {
        public int Id { get; set; }
        public string CitizenName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? RawCategory { get; set; }
        public bool HasFeedback { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string? BarangayName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int LocationDensityScore { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string? FirstAttachmentPath { get; set; }
    }
}
