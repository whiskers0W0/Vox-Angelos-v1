using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        [BindProperty] public string Description { get; set; } = string.Empty;
        [BindProperty] public string? LocationName { get; set; }
        [BindProperty] public double? Latitude { get; set; }
        [BindProperty] public double? Longitude { get; set; }
        [BindProperty] public List<IFormFile> Attachments { get; set; } = new();
        [BindProperty] public string RecJustification { get; set; } = string.Empty;
        [BindProperty] public string RecCategory { get; set; } = string.Empty;
        [BindProperty] public string RecTitle { get; set; } = string.Empty;
        [BindProperty] public string RecLocation { get; set; } = string.Empty;
        [BindProperty] public string RecDescription { get; set; } = string.Empty;
        [BindProperty] public string RecBeneficiaries { get; set; } = string.Empty;
        [BindProperty] public int RecPeopleAffected { get; set; }
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

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            if (string.IsNullOrWhiteSpace(Description))
                ModelState.AddModelError("Description", "Description is required.");
            if (string.IsNullOrWhiteSpace(LocationName) || Latitude == null || Longitude == null)
                ModelState.AddModelError("LocationName", "Please pin your location using the map.");
            if (Attachments == null || Attachments.Count == 0)
                ModelState.AddModelError("Attachments", "Please upload at least one image or video.");

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var concern = new Concern
            {
                CitizenId = user.Id,
                Description = Description,
                LocationName = LocationName,
                Latitude = Latitude,
                Longitude = Longitude,
                Status = "Unresolved",
                Category = await _classifier.ClassifyAsync(
                    Description,
                    ResolveCredentialsPath(_configuration["GoogleCloud:CredentialsPath"])),
                SubmittedAt = DateTime.UtcNow
            };

            _db.Concerns.Add(concern);
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
                    var fileType = file.ContentType.StartsWith("video") ? "video" : "image";
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
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var classificationText = $"{RecTitle} {RecDescription} {RecJustification}";

            var recommendation = new Recommendation
            {
                CitizenId = user.Id,
                Justification = RecJustification,
                Category = RecCategory,
                Title = RecTitle,
                Location = RecLocation,
                Description = RecDescription,
                Beneficiaries = RecBeneficiaries,
                EstimatedPeopleAffected = RecPeopleAffected,
                Status = "Pending",
                AssignedOffice = await _classifier.ClassifyAsync(
                    classificationText,
                    ResolveCredentialsPath(_configuration["GoogleCloud:CredentialsPath"])),
                SubmittedAt = DateTime.UtcNow
            };

            _db.Recommendations.Add(recommendation);
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
    }
}
