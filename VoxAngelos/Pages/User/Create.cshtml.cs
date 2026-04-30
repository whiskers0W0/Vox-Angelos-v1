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

            TempData["ConcernSuccess"] = "Your concern has been submitted successfully!";
            return RedirectToPage();
        }
    }
}