using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;
using Microsoft.EntityFrameworkCore;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("concern-submission")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly ConcernClassificationService _classifier;
        private readonly UrgencyScoreService _urgencyScore;
        private readonly IHubContext<FeedHub> _feedHub;

        public CreateModel(ApplicationDbContext db,
                           UserManager<ApplicationUser> userManager,
                           IWebHostEnvironment env,
                           IConfiguration configuration,
                           ConcernClassificationService classifier,
                           UrgencyScoreService urgencyScore,
                           IHubContext<FeedHub> feedHub)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
            _configuration = configuration;
            _classifier = classifier;
            _urgencyScore = urgencyScore;
            _feedHub = feedHub;
        }

        public string CitizenFullName { get; set; } = string.Empty;
        public string GoogleMapsApiKey => _configuration["GoogleMaps:ApiKey"] ?? "";
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
        [BindProperty] public string? RecJustification { get; set; }
        [BindProperty] public string? RecCategory { get; set; }
        [BindProperty] public string? RecTitle { get; set; }
        [BindProperty] public string? RecLocation { get; set; }
        [BindProperty] public string? RecDescription { get; set; }
        [BindProperty] public string? RecBeneficiaries { get; set; }
        [BindProperty] public int RecPeopleAffected { get; set; }
        [BindProperty] public bool RecIsAnonymous { get; set; }
        [BindProperty] public List<IFormFile> RecAttachments { get; set; } = new();

        private string? ResolveCredentialsPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(_env.ContentRootPath, path);
        }

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
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

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
            if (Attachments == null || Attachments.Count == 0)
                ModelState.AddModelError("Attachments", "Please upload at least one image or video.");

            const long maximumVideoSizeInBytes = 100 * 1024 * 1024;
            var oversizedVideo = Attachments?.FirstOrDefault(file =>
                file.Length > maximumVideoSizeInBytes &&
                file.ContentType.StartsWith("video", StringComparison.OrdinalIgnoreCase));

            if (oversizedVideo != null)
                ModelState.AddModelError("Attachments", "The uploaded video exceeds the maximum allowed size (100 MB). Please upload a smaller video.");

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

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
            concern.Category = !string.IsNullOrWhiteSpace(ConfirmedCategory)
                ? ConfirmedCategory
                : await _classifier.ClassifyAsync(
                    Description,
                    ResolveCredentialsPath(_configuration["GoogleCloud:CredentialsPath"]));
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

            if (Attachments != null && Attachments.Count > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "concerns");
                Directory.CreateDirectory(uploadsFolder);

                foreach (var file in Attachments)
                {
                    if (file.Length == 0) continue;
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                    var fileType = file.ContentType.StartsWith("video") ? "video"
                                 : file.ContentType.StartsWith("image") ? "image"
                                 : "document";
                    _db.ConcernAttachments.Add(new ConcernAttachment
                    {
                        ConcernId = concern.Id,
                        FilePath = $"/uploads/concerns/{fileName}",
                        FileType = fileType,
                        UploadedAt = DateTime.UtcNow
                    });
                }
                await _db.SaveChangesAsync();
            }

            await _urgencyScore.ApplyLocationAsync(concern);

            // Push the LGU dashboard(s) that will show this concern (LGU/Index.cshtml.cs
            // lists a department's own concerns plus any still-unclassified ones, so an
            // unclassified submission needs to reach every department's group).
            var notifyDepartments = concern.Category != null
                ? new[] { concern.Category }
                : ConcernClassificationService.Departments;
            foreach (var dept in notifyDepartments)
                await _feedHub.Clients.Group(FeedHub.LguDepartmentGroup(dept)).SendAsync("ConcernFeedChanged");

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

        public class ClassifyRequest
        {
            public string Description { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnPostClassifyAsync([FromBody] ClassifyRequest request)
        {
            var category = await _classifier.ClassifyAsync(
                request.Description,
                ResolveCredentialsPath(_configuration["GoogleCloud:CredentialsPath"]));

            if (category == null)
                return new JsonResult(new { success = false });

            var officeNames = new Dictionary<string, string>
            {
                ["SWDO"] = "Social Welfare and Development Office",
                ["CEO"] = "City Engineer's Office",
                ["CENRO"] = "City Environment and Natural Resources Office",
                ["ACDO"] = "City Development / Urban Planning Office",
                ["PPTRO"] = "Public Safety, Traffic and Transport Regulation Office",
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
                email = officeEmail ?? ""
            });
        }

        public async Task<IActionResult> OnPostRecommendationAsync()
        {
            Console.WriteLine("Reached OnPostRecommendationAsync");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

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
            recommendation.AssignedOffice = await _classifier.ClassifyAsync(
                classificationText,
                ResolveCredentialsPath(_configuration["GoogleCloud:CredentialsPath"]));
            recommendation.SubmittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            if (RecAttachments != null && RecAttachments.Count > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "recommendations");
                Directory.CreateDirectory(uploadsFolder);

                foreach (var file in RecAttachments)
                {
                    if (file.Length == 0) continue;
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    var fileType = file.ContentType.StartsWith("video") ? "video"
                                 : file.ContentType.StartsWith("image") ? "image"
                                 : "document";

                    _db.RecommendationAttachments.Add(new RecommendationAttachment
                    {
                        RecommendationId = recommendation.Id,
                        FilePath = $"/uploads/recommendations/{fileName}",
                        FileType = fileType,
                        UploadedAt = DateTime.UtcNow
                    });
                }
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
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
    }
}
