using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoxAngelos.Data;
using Microsoft.EntityFrameworkCore;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public CreateModel(ApplicationDbContext db,
                           UserManager<ApplicationUser> userManager,
                           IWebHostEnvironment env)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
        }

        // Displayed in the form header
        public string CitizenFullName { get; set; } = string.Empty;

        // Bound from the form
        [BindProperty] public string Description { get; set; } = string.Empty;
        [BindProperty] public string? LocationName { get; set; }
        [BindProperty] public double? Latitude { get; set; }
        [BindProperty] public double? Longitude { get; set; }
        [BindProperty] public List<IFormFile> Attachments { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            // Pull full name from UserProfile
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
            {
                ModelState.AddModelError("Description", "Description is required.");
            }

            if (string.IsNullOrWhiteSpace(LocationName) || Latitude == null || Longitude == null)
            {
                ModelState.AddModelError("LocationName", "Please pin your location using the map.");
            }

            if (Attachments == null || Attachments.Count == 0)
            {
                ModelState.AddModelError("Attachments", "Please upload at least one image or video.");
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            // Save concern
            var concern = new Concern
            {
                CitizenId = user.Id,
                Description = Description,
                LocationName = LocationName,
                Latitude = Latitude,
                Longitude = Longitude,
                Status = "Unresolved",
                Category = null, // NLP will fill this later
                SubmittedAt = DateTime.UtcNow
            };

            _db.Concerns.Add(concern);
            await _db.SaveChangesAsync(); // Save first to get concern.Id

            // Save attachments
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

            // Redirect back to form (confirmation page can replace this later)
            TempData["ConcernSuccess"] = "Your concern has been submitted successfully!";
            return RedirectToPage();
        }
    }
}