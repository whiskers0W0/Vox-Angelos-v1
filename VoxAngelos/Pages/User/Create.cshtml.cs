using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoxAngelos.Data;
using VoxAngelos.Services;
using Microsoft.EntityFrameworkCore;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly ConcernClassificationService _classifier;

        public CreateModel(ApplicationDbContext db,
                           UserManager<ApplicationUser> userManager,
                           IWebHostEnvironment env,
                           IConfiguration configuration,
                           ConcernClassificationService classifier)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
            _configuration = configuration;
            _classifier = classifier;
        }

        public string CitizenFullName { get; set; } = string.Empty;
        public string GoogleMapsApiKey => _configuration["GoogleMaps:ApiKey"] ?? "";

        [BindProperty] public string Description { get; set; } = string.Empty;
        [BindProperty] public string? LocationName { get; set; }
        [BindProperty] public double? Latitude { get; set; }
        [BindProperty] public double? Longitude { get; set; }
        [BindProperty] public List<IFormFile> Attachments { get; set; } = new();

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

        public async Task<IActionResult> OnPostClassifyAsync([FromBody] ClassifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Description))
                return new JsonResult(new { success = false, error = "Description is required." });

            try
            {
                var client = Google.Cloud.Language.V1.LanguageServiceClient.Create();
                var googleDoc = new Google.Cloud.Language.V1.Document
                {
                    Content = PadToMinimumWords(request.Description),
                    Type = Google.Cloud.Language.V1.Document.Types.Type.PlainText
                };

                var response = await client.ClassifyTextAsync(googleDoc);
                var bestMatch = response.Categories
                    .OrderByDescending(c => c.Confidence)
                    .FirstOrDefault();

                string category;
                string office;
                string email;

                if (bestMatch != null)
                {
                    category = bestMatch.Name;
                    (office, email) = MapToOffice(bestMatch.Name);
                }
                else
                {
                    (category, office, email) = ClassifyByKeywords(request.Description);
                }

                return new JsonResult(new { success = true, category, office, email });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("NLP Error: " + ex.Message);
                var (category, office, email) = ClassifyByKeywords(request.Description);
                return new JsonResult(new { success = true, category, office, email });
            }
        }

        private string PadToMinimumWords(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 20) return text;
            var padding = "This is a community concern report submitted by a resident regarding a local issue that requires attention from the appropriate government office.";
            return text + " " + padding;
        }

        private static (string Category, string Office, string Email) ClassifyByKeywords(string text)
        {
            text = text.ToLower();

            var rules = new[]
            {
                (Keywords: new[] { "sick", "sakit", "ospital", "gamot", "dengue", "rabies",
                                   "doktor", "clinic", "fever", "lagnat", "medical", "health",
                                   "masakit", "sanitation" },
                 Category: "Health", Office: "City Health Office", Email: "health@voxangelos.gov.ph"),

                (Keywords: new[] { "daan", "road", "pothole", "tubo", "drainage", "baha",
                                   "flood", "kalsada", "tulay", "bridge", "ilaw", "streetlight",
                                   "kuryente", "gripo", "leaking", "broken", "repair", "sidewalk" },
                 Category: "Infrastructure", Office: "Engineering Office", Email: "engineering@voxangelos.gov.ph"),

                (Keywords: new[] { "robbery", "holdap", "droga", "drugs", "patay", "crime",
                                   "pulis", "police", "violence", "away", "saksak",
                                   "suspicious", "magnanakaw", "nakaw", "vandal", "banta" },
                 Category: "Public Safety", Office: "Public Safety Office", Email: "publicsafety@voxangelos.gov.ph"),

                (Keywords: new[] { "basura", "garbage", "polusyon", "pollution", "ilog", "river",
                                   "farm", "tanim", "hayop", "animal", "isda", "usok", "smoke",
                                   "puno", "tree", "dumping", "waste", "kalat", "lupa", "soil" },
                 Category: "Environment/Agriculture", Office: "Agriculture Office", Email: "agriculture@voxangelos.gov.ph"),

                (Keywords: new[] { "mahirap", "poor", "matanda", "elderly", "ulila", "orphan",
                                   "tulong", "help", "assistance", "welfare", "ayuda", "gutom",
                                   "hungry", "homeless", "bata", "abused", "pwd" },
                 Category: "Social Welfare", Office: "Social Welfare Office", Email: "socialwelfare@voxangelos.gov.ph"),
            };

            var best = rules
                .Select(r => new {
                    r.Category,
                    r.Office,
                    r.Email,
                    Score = r.Keywords.Count(kw => text.Contains(kw))
                })
                .Where(r => r.Score > 0)
                .OrderByDescending(r => r.Score)
                .FirstOrDefault();

            return best != null
                ? (best.Category, best.Office, best.Email)
                : ("General", "General Services Office", "engineering@voxangelos.gov.ph");
        }

        public class ClassifyRequest
        {
            public string Description { get; set; } = string.Empty;
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

            var confirmedCategory = Request.Form["ConfirmedCategory"].ToString();
            var confirmedOffice = Request.Form["ConfirmedOffice"].ToString();

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

            TempData["ConcernSuccess"] = "Your concern has been submitted successfully!";
            return RedirectToPage();
        }
    }
}