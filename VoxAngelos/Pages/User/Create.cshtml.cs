using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ConcernClassificationService _classifier;
        private readonly UrgencyScoreService _urgencyScore;
        private readonly IHubContext<FeedHub> _feedHub;
        private readonly CloudinaryAttachmentStorage _attachmentStorage;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CreateModel> _logger;
        private readonly IEmailSender _emailSender;
        private const int MaximumAttachmentCount = 8;
        private static readonly HashSet<string> ConcernCategoryHints = new(StringComparer.Ordinal)
        {
            "Infrastructure & Public Works",
            "Environment & Sanitation",
            "Community & Social Welfare",
            "Public Safety, Traffic & Transport",
            "Urban Planning & Public Spaces",
            "Senior Citizen Support",
            "PWD Accessibility & Services",
            "Unsure"
        };

        public CreateModel(ApplicationDbContext db,
                           UserManager<ApplicationUser> userManager,
                           IConfiguration configuration,
                           ConcernClassificationService classifier,
                           UrgencyScoreService urgencyScore,
                           IHubContext<FeedHub> feedHub,
                           CloudinaryAttachmentStorage attachmentStorage,
                           IWebHostEnvironment env,
                           ILogger<CreateModel> logger,
                           IEmailSender emailSender)
        {
            _db = db;
            _userManager = userManager;
            _configuration = configuration;
            _classifier = classifier;
            _urgencyScore = urgencyScore;
            _feedHub = feedHub;
            _attachmentStorage = attachmentStorage;
            _env = env;
            _logger = logger;
            _emailSender = emailSender;
        }

        public string CitizenFullName { get; set; } = string.Empty;
        public string GoogleMapsApiKey => _configuration["GoogleMaps:ApiKey"] ?? "";
        public bool IsDevelopmentEnvironment => _env.IsDevelopment();
        public Concern? SavedConcernDraft { get; private set; }
        public Recommendation? SavedRecommendationDraft { get; private set; }

        // This page hosts two independent forms (Concern and Recommendation) on one
        // PageModel. Each handler validates only its own fields manually — but with
        // nullable reference types enabled, ASP.NET Core implicitly treats every
        // non-nullable string [BindProperty] as required for ModelState.IsValid,
        // regardless of which form actually posted ([ValidateNever] does NOT suppress
        // this implicit check). Declaring each side's string fields nullable (like
        // LocationName already was) is what actually avoids it.
        [BindProperty] public string Description { get; set; } = string.Empty;
        [BindProperty] public string? LocationName { get; set; }
        [BindProperty] public double? Latitude { get; set; }
        [BindProperty] public double? Longitude { get; set; }
        [BindProperty] public List<IFormFile> Attachments { get; set; } = new();
        [BindProperty] public string? ConfirmedCategory { get; set; }
        [BindProperty] public string? ConcernCategoryHint { get; set; }
        [BindProperty] public string? RecJustification { get; set; }
        [BindProperty] public string? RecCategory { get; set; }
        [BindProperty] public string? RecTitle { get; set; }
        [BindProperty] public string? RecLocation { get; set; }
        [BindProperty] public string? RecDescription { get; set; }
        [BindProperty] public string? RecBeneficiaries { get; set; }
        [BindProperty] public int RecPeopleAffected { get; set; }
        [BindProperty] public bool RecIsAnonymous { get; set; }
        [BindProperty] public List<IFormFile> RecAttachments { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var profile = await _db.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            CitizenFullName = profile != null
                ? $"{profile.FirstName} {profile.LastName}"
                : user.Email ?? "Citizen";

            SavedRecommendationDraft = await _db.Recommendations
                .AsNoTracking()
                .Where(r => r.CitizenId == user.Id && r.Status == "Draft")
                .OrderByDescending(r => r.SubmittedAt)
                .FirstOrDefaultAsync();

            SavedConcernDraft = await _db.Concerns
                .AsNoTracking()
                .Where(c => c.CitizenId == user.Id && c.Status == "Draft")
                .OrderByDescending(c => c.SubmittedAt)
                .FirstOrDefaultAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var isAjaxRequest = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return isAjaxRequest
                    ? new JsonResult(new { success = false, message = "Your session has expired. Please sign in again." }) { StatusCode = StatusCodes.Status401Unauthorized }
                    : RedirectToPage("/Login");
            }

            // This handler submits only a concern. Ignore validation generated for
            // the separate recommendation form that shares this Razor Page.
            foreach (var recommendationField in new[]
            {
                nameof(RecJustification),
                nameof(RecCategory),
                nameof(RecTitle),
                nameof(RecLocation),
                nameof(RecDescription),
                nameof(RecBeneficiaries)
            })
            {
                ModelState.Remove(recommendationField);
            }

            if (string.IsNullOrWhiteSpace(Description))
                ModelState.AddModelError("Description", "Description is required.");
            if (string.IsNullOrWhiteSpace(LocationName) || Latitude == null || Longitude == null)
                ModelState.AddModelError("LocationName", "Please pin your location using the map.");
            else if (!await IsWithinAngelesCityAsync(Latitude.Value, Longitude.Value))
                ModelState.AddModelError("LocationName", "Please select a location within Angeles City only.");
            if (Attachments == null || Attachments.Count == 0)
                ModelState.AddModelError("Attachments", "Please upload at least one image or video.");
            if (Attachments != null && Attachments.Count > MaximumAttachmentCount)
                ModelState.AddModelError("Attachments", $"You can upload a maximum of {MaximumAttachmentCount} attachments.");

            const long maximumVideoSizeInBytes = 100 * 1024 * 1024;
            var oversizedVideo = Attachments?.FirstOrDefault(file =>
                file.Length > maximumVideoSizeInBytes &&
                file.ContentType.StartsWith("video", StringComparison.OrdinalIgnoreCase));

            if (oversizedVideo != null)
                ModelState.AddModelError("Attachments", "The uploaded video exceeds the maximum allowed size (100 MB). Please upload a smaller video.");

            if (!ModelState.IsValid)
            {
                if (isAjaxRequest)
                {
                    var message = ModelState.Values
                        .SelectMany(value => value.Errors)
                        .Select(error => error.ErrorMessage)
                        .FirstOrDefault() ?? "Your concern could not be submitted. Please check the form and try again.";
                    return BadRequest(new { success = false, message });
                }

                await OnGetAsync();
                return Page();
            }

            string? classifiedCategory;
            if (!string.IsNullOrWhiteSpace(ConfirmedCategory))
            {
                classifiedCategory = ConfirmedCategory;
            }
            else
            {
                var classification = await _classifier.ClassifyAsync(Description);
                if (classification.IsRejected)
                {
                    var rejectionMessage = classification.RejectionReason
                        ?? "Your concern could not be submitted. Please check the form and try again.";

                    if (isAjaxRequest)
                        return BadRequest(new { success = false, message = rejectionMessage });

                    ModelState.AddModelError("Description", rejectionMessage);
                    await OnGetAsync();
                    return Page();
                }

                classifiedCategory = classification.Department;
            }

            // Upload every attachment before writing the concern to the database.
            // A Cloudinary failure therefore cannot leave a submitted concern with
            // missing attachment records.
            var uploadedAttachments = new List<CloudinaryAttachmentUpload>();
            try
            {
                foreach (var file in Attachments!)
                {
                    if (file.Length == 0) continue;
                    uploadedAttachments.Add(await _attachmentStorage.UploadAsync(file, "concerns"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Concern attachment upload failed for user {UserId}.", user.Id);
                const string uploadError =
                    "We could not upload your attachment, so your concern was not submitted. Please try again.";

                if (isAjaxRequest)
                {
                    return new JsonResult(new { success = false, message = uploadError })
                    {
                        StatusCode = StatusCodes.Status503ServiceUnavailable
                    };
                }

                ModelState.AddModelError("Attachments", uploadError);
                await OnGetAsync();
                return Page();
            }

            var executionStrategy = _db.Database.CreateExecutionStrategy();
            IEnumerable<string> notifyDepartments = Array.Empty<string>();

            await executionStrategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var submissionTransaction = await _db.Database.BeginTransactionAsync();

                var concern = await _db.Concerns
                    .FirstOrDefaultAsync(c => c.CitizenId == user.Id && c.Status == "Draft");

                if (concern == null)
                {
                    concern = new Concern
                    {
                        CitizenId = user.Id
                    };
                    _db.Concerns.Add(concern);
                }

                concern.Description = Description;
                concern.LocationName = LocationName;
                concern.Latitude = Latitude;
                concern.Longitude = Longitude;
                concern.Status = "Unresolved";
                concern.Category = classifiedCategory;
                concern.SubmittedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concern.Id,
                    EventType = "Submitted",
                    Status = concern.Status,
                    Message = "Your concern was submitted and is awaiting review.",
                    ActorRole = "Citizen",
                    ActorName = user.UserName ?? user.Email ?? "Citizen",
                    CreatedAt = concern.SubmittedAt
                });
                await _db.SaveChangesAsync();

                foreach (var uploadedAttachment in uploadedAttachments)
                {
                    _db.ConcernAttachments.Add(new ConcernAttachment
                    {
                        ConcernId = concern.Id,
                        FilePath = uploadedAttachment.FilePath,
                        FileType = uploadedAttachment.FileType,
                        UploadedAt = DateTime.UtcNow
                    });
                }
                await _db.SaveChangesAsync();

                // When Admin triage is enabled, unclassified submissions stay in the
                // Admin routing queue until an office has deliberately assigned them.
                notifyDepartments = concern.Category != null
                    ? new[] { concern.Category }
                    : _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized")
                        ? Array.Empty<string>()
                        : ConcernClassificationService.Departments;

                await AddLguSubmissionNotificationsAsync(
                    notifyDepartments,
                    title: "New concern received",
                    message: "A new citizen concern has been assigned to your office for review.",
                    notificationType: "IncomingConcern",
                    senderName: user.UserName ?? user.Email ?? "Citizen",
                    linkUrl: "/LGU/Index?filter=Unresolved");
                await _db.SaveChangesAsync();

                await _urgencyScore.ApplyLocationAsync(concern);
                await submissionTransaction.CommitAsync();
            });

            await SendLguSubmissionEmailsAsync(
                notifyDepartments,
                subject: "New citizen concern assigned",
                heading: "New concern received",
                message: "A new citizen concern has been assigned to your office for review.",
                linkUrl: "/LGU/Index?filter=Unresolved");

            if (!notifyDepartments.Any() && _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized"))
                await NotifyAdminsOfUnassignedSubmissionAsync("concern");

            foreach (var dept in notifyDepartments)
                await _feedHub.Clients.Group(FeedHub.LguDepartmentGroup(dept)).SendAsync("ConcernFeedChanged");

            if (isAjaxRequest)
                return new JsonResult(new { success = true });

            TempData["ConcernSuccess"] = "Your concern has been submitted successfully!";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveConcernDraftAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var draft = await _db.Concerns
                .FirstOrDefaultAsync(c => c.CitizenId == user.Id && c.Status == "Draft");

            if (draft == null)
            {
                draft = new Concern
                {
                    CitizenId = user.Id,
                    Status = "Draft",
                    SubmittedAt = DateTime.UtcNow
                };
                _db.Concerns.Add(draft);
            }

            draft.Description = Description ?? string.Empty;
            draft.Category = !string.IsNullOrWhiteSpace(ConcernCategoryHint)
                && ConcernCategoryHints.Contains(ConcernCategoryHint)
                    ? ConcernCategoryHint
                    : null;
            draft.LocationName = LocationName;
            draft.Latitude = Latitude;
            draft.Longitude = Longitude;

            await _db.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                message = "Your concern draft has been saved."
            });
        }

        public async Task<IActionResult> OnPostDeleteConcernDraftAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return new JsonResult(new { success = false, message = "Your session has expired. Please sign in again." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            var drafts = await _db.Concerns
                .Where(concern => concern.CitizenId == user.Id && concern.Status == "Draft")
                .ToListAsync();

            if (drafts.Count > 0)
            {
                _db.Concerns.RemoveRange(drafts);
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true, message = "Your concern draft has been deleted." });
        }

        public class ClassifyRequest
        {
            public string Description { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnPostClassifyAsync([FromBody] ClassifyRequest request)
        {
            var classification = await _classifier.ClassifyAsync(request.Description);

            if (classification.IsRejected)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = classification.RejectionReason
                });
            }

            var category = classification.Department;

            if (category == null)
            {
                // A legitimate concern — just not yet mapped to a real office (or the HF
                // Space was unavailable) — is still submittable as Uncategorized for manual
                // LGU triage, unlike an IsRejected result above.
                return new JsonResult(new
                {
                    success = true,
                    category = (string?)null,
                    office = "Uncategorized — pending manual review",
                    email = "",
                    classifierTier = classification.Tier,
                    confidence = classification.Confidence
                });
            }

            var officeNames = new Dictionary<string, string>
            {
                ["SWDO"] = "Social Welfare and Development Office",
                ["CEO"] = "City Engineer's Office",
                ["CENRO"] = "City Environment and Natural Resources Office",
                ["ACDO"] = "City Development / Urban Planning Office",
                ["PTRO"] = "Public Safety, Traffic and Transport Regulation Office",
                ["OSCA"] = "Office of Senior Citizens Affairs",
                ["PWDAO"] = "Persons With Disability Affairs Office"
            };

            var officeEmail = await _userManager.Users
                .Where(u => u.Department == category)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();

            return new JsonResult(new
            {
                success = true,
                category,
                office = officeNames.GetValueOrDefault(category, category),
                email = officeEmail ?? "",
                classifierTier = classification.Tier,
                confidence = classification.Confidence
            });
        }

        public async Task<IActionResult> OnPostRecommendationAsync()
        {
            Console.WriteLine("Reached OnPostRecommendationAsync");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            if (RecAttachments != null && RecAttachments.Count > MaximumAttachmentCount)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"You can upload a maximum of {MaximumAttachmentCount} attachments."
                });
            }

            const long maximumVideoSizeInBytes = 100 * 1024 * 1024;
            var oversizedVideo = RecAttachments?.FirstOrDefault(file =>
                file.Length > maximumVideoSizeInBytes &&
                file.ContentType.StartsWith("video", StringComparison.OrdinalIgnoreCase));

            if (oversizedVideo != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "The uploaded video exceeds the maximum allowed size (100 MB). Please upload a smaller video."
                });
            }

            var classificationText = $"{RecTitle} {RecDescription} {RecJustification}";

            // Classify before uploading attachments (and outside the retryable database unit
            // below) so a rejected submission fails fast without wasting a Cloudinary upload,
            // and a transient DB retry never re-sends the same text to the classifier twice.
            var recommendationClassification = await _classifier.ClassifyAsync(classificationText);
            if (recommendationClassification.IsRejected)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = recommendationClassification.RejectionReason
                });
            }

            // Upload attachments before changing a draft into a submitted
            // recommendation. This makes Cloudinary failures visible to the client
            // and avoids saving a recommendation whose attachment rows are missing.
            var uploadedAttachments = new List<CloudinaryAttachmentUpload>();
            try
            {
                foreach (var file in RecAttachments ?? Enumerable.Empty<IFormFile>())
                {
                    if (file.Length == 0) continue;
                    uploadedAttachments.Add(
                        await _attachmentStorage.UploadAsync(file, "recommendations"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Recommendation attachment upload failed for user {UserId}.",
                    user.Id);

                return new JsonResult(new
                {
                    success = false,
                    message = "We could not upload your attachments to Cloudinary, so your recommendation was not submitted. Please try again."
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
            }

            var assignedOffice = recommendationClassification.Department;
            var executionStrategy = _db.Database.CreateExecutionStrategy();

            await executionStrategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var submissionTransaction = await _db.Database.BeginTransactionAsync();

            var recommendation = await _db.Recommendations
                .FirstOrDefaultAsync(r => r.CitizenId == user.Id && r.Status == "Draft");

            if (recommendation == null)
            {
                recommendation = new Recommendation
                {
                    CitizenId = user.Id
                };
                _db.Recommendations.Add(recommendation);
            }

            recommendation.Justification = RecJustification ?? string.Empty;
            recommendation.Category = RecCategory ?? string.Empty;
            recommendation.Title = RecTitle ?? string.Empty;
            recommendation.Location = RecLocation ?? string.Empty;
            recommendation.Description = RecDescription ?? string.Empty;
            recommendation.Beneficiaries = RecBeneficiaries ?? string.Empty;
            recommendation.EstimatedPeopleAffected = RecPeopleAffected;
            recommendation.IsAnonymous = RecIsAnonymous;
            recommendation.Status = "Pending";
            recommendation.AssignedOffice = assignedOffice;
            recommendation.SubmittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            foreach (var uploadedAttachment in uploadedAttachments)
            {
                _db.RecommendationAttachments.Add(new RecommendationAttachment
                {
                    RecommendationId = recommendation.Id,
                    FilePath = uploadedAttachment.FilePath,
                    FileType = uploadedAttachment.FileType,
                    UploadedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(recommendation.AssignedOffice))
            {
                await AddLguSubmissionNotificationsAsync(
                    new[] { recommendation.AssignedOffice },
                    title: "New recommendation received",
                    message: $"A new citizen recommendation, “{recommendation.Title},” is ready for your office to review.",
                    notificationType: "IncomingRecommendation",
                    senderName: user.UserName ?? user.Email ?? "Citizen",
                    linkUrl: "/LGU/ReviewRecommendations");
                await _db.SaveChangesAsync();

            }

                await submissionTransaction.CommitAsync();
            });

            if (!string.IsNullOrWhiteSpace(assignedOffice))
            {
                await SendLguSubmissionEmailsAsync(
                    new[] { assignedOffice },
                    subject: "New citizen recommendation assigned",
                    heading: "New recommendation received",
                    message: $"A new citizen recommendation, “{RecTitle},” is ready for your office to review.",
                    linkUrl: "/LGU/ReviewRecommendations");
            }
            else if (_configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized"))
            {
                await NotifyAdminsOfUnassignedSubmissionAsync("recommendation");
            }

            if (!string.IsNullOrWhiteSpace(assignedOffice))
            {
                await _feedHub.Clients
                    .Group(FeedHub.LguDepartmentGroup(assignedOffice))
                    .SendAsync("RecommendationFeedChanged");
            }

            return new JsonResult(new
            {
                success = true,
                office = assignedOffice ?? "",
                classifierTier = recommendationClassification.Tier,
                confidence = recommendationClassification.Confidence
            });
        }

        private async Task NotifyAdminsOfUnassignedSubmissionAsync(string submissionType)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            var activeAdmins = admins.Where(admin => admin.LockoutEnd == null || admin.LockoutEnd < DateTimeOffset.UtcNow).ToList();
            foreach (var admin in activeAdmins)
            {
                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = admin.Id,
                    Title = $"Unassigned {submissionType} needs routing",
                    Message = $"A citizen {submissionType} could not be assigned automatically and is waiting in the Admin routing queue.",
                    NotificationType = "AdminRouting",
                    SenderRole = "System",
                    SenderName = "Vox Angelos",
                    LinkUrl = "/Admin/NlpAccuracy",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();

            foreach (var admin in activeAdmins.Where(admin => admin.EmailNotificationsEnabled && admin.EmailConfirmed && !string.IsNullOrWhiteSpace(admin.Email)))
            {
                try
                {
                    await _emailSender.SendEmailAsync(admin.Email!, $"Unassigned Vox Angelos {submissionType}",
                        $"<p>A new citizen <strong>{submissionType}</strong> could not be assigned automatically.</p><p>Sign in to the Admin portal and open NLP &amp; Routing to select the responsible office.</p>");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Admin routing alert email failed for {AdminId}.", admin.Id);
                }
            }
        }

        private async Task AddLguSubmissionNotificationsAsync(
            IEnumerable<string> departments,
            string title,
            string message,
            string notificationType,
            string senderName,
            string linkUrl)
        {
            var departmentCodes = departments
                .Where(department => !string.IsNullOrWhiteSpace(department))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (departmentCodes.Count == 0)
                return;

            var recipientIds = await _userManager.Users
                .AsNoTracking()
                .Where(account => (account.LockoutEnd == null || account.LockoutEnd < DateTimeOffset.UtcNow)
                    && account.Department != null
                    && departmentCodes.Contains(account.Department!))
                .Select(account => account.Id)
                .ToListAsync();

            foreach (var recipientId in recipientIds)
            {
                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recipientId,
                    Title = title,
                    Message = message,
                    NotificationType = notificationType,
                    SenderRole = "Citizen",
                    SenderName = senderName,
                    LinkUrl = linkUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private async Task<bool> IsWithinAngelesCityAsync(double latitude, double longitude)
        {
            var geoJsonPath = Path.Combine(
                _env.WebRootPath,
                "geojson",
                "angeles-city-barangays.geojson");

            if (!System.IO.File.Exists(geoJsonPath))
            {
                _logger.LogError("Angeles City barangay boundary file was not found at {GeoJsonPath}.", geoJsonPath);
                return false;
            }

            await using var stream = System.IO.File.OpenRead(geoJsonPath);
            using var document = await JsonDocument.ParseAsync(stream);

            foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                var geometry = feature.GetProperty("geometry");
                var geometryType = geometry.GetProperty("type").GetString();
                var coordinates = geometry.GetProperty("coordinates");

                var containsPoint = geometryType switch
                {
                    "Polygon" => IsPointInPolygon(longitude, latitude, coordinates),
                    "MultiPolygon" => coordinates.EnumerateArray()
                        .Any(polygon => IsPointInPolygon(longitude, latitude, polygon)),
                    _ => false
                };

                if (containsPoint) return true;
            }

            return false;
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
            if (pointCount < 3) return false;

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

        private async Task SendLguSubmissionEmailsAsync(
            IEnumerable<string> departments,
            string subject,
            string heading,
            string message,
            string linkUrl)
        {
            var departmentCodes = departments
                .Where(department => !string.IsNullOrWhiteSpace(department))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (departmentCodes.Count == 0) return;

            var recipients = await _userManager.Users
                .AsNoTracking()
                .Where(account => account.Department != null &&
                    departmentCodes.Contains(account.Department!) &&
                    account.EmailNotificationsEnabled &&
                    account.EmailConfirmed &&
                    account.Email != null &&
                    (account.LockoutEnd == null || account.LockoutEnd < DateTimeOffset.UtcNow))
                .Select(account => new { account.Id, account.Email })
                .ToListAsync();

            var publicBaseUrl = _configuration["App:PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            var htmlMessage = BuildLguNotificationEmail(heading, message, linkUrl, publicBaseUrl);

            foreach (var recipient in recipients)
            {
                try
                {
                    await _emailSender.SendEmailAsync(recipient.Email!, subject, htmlMessage);
                }
                catch (Exception exception)
                {
                    // Email is supplementary; the saved submission and in-app
                    // notification must remain successful if the provider is down.
                    _logger.LogWarning(
                        exception,
                        "Could not send an optional LGU assignment email to user {UserId}.",
                        recipient.Id);
                }
            }
        }

        private static string BuildLguNotificationEmail(string heading, string message, string linkUrl, string publicBaseUrl)
        {
            string baseUrl = publicBaseUrl?.TrimEnd('/') ?? "";
            var safeHeading = WebUtility.HtmlEncode(heading);
            var safeMessage = WebUtility.HtmlEncode(message);

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
                LGU Notification
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
                    <a href=""{baseUrl}{linkUrl}""
                       style=""display:inline-block; padding:14px 28px; color:#ffffff; font-size:13px; font-weight:700; letter-spacing:0.02em; text-transform:uppercase; text-decoration:none; border-radius:8px;"">
                      Open LGU Review Queue
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
                You received this because email alerts are enabled in your Vox Angelos LGU notifications.
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

        public async Task<IActionResult> OnPostSaveRecommendationDraftAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var draft = await _db.Recommendations
                .FirstOrDefaultAsync(r => r.CitizenId == user.Id && r.Status == "Draft");

            if (draft == null)
            {
                draft = new Recommendation
                {
                    CitizenId = user.Id,
                    Status = "Draft",
                    SubmittedAt = DateTime.UtcNow
                };
                _db.Recommendations.Add(draft);
            }

            draft.Justification = RecJustification ?? string.Empty;
            draft.Category = RecCategory ?? string.Empty;
            draft.Title = RecTitle ?? string.Empty;
            draft.Location = RecLocation ?? string.Empty;
            draft.Description = RecDescription ?? string.Empty;
            draft.Beneficiaries = RecBeneficiaries ?? string.Empty;
            draft.EstimatedPeopleAffected = RecPeopleAffected;
            draft.IsAnonymous = RecIsAnonymous;

            await _db.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                message = "Your recommendation draft has been saved."
            });
        }

        public async Task<IActionResult> OnPostDeleteRecommendationDraftAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return new JsonResult(new { success = false, message = "Your session has expired. Please sign in again." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            var drafts = await _db.Recommendations
                .Where(recommendation => recommendation.CitizenId == user.Id && recommendation.Status == "Draft")
                .ToListAsync();

            if (drafts.Count > 0)
            {
                _db.Recommendations.RemoveRange(drafts);
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true, message = "Your recommendation draft has been deleted." });
        }
    }
}
