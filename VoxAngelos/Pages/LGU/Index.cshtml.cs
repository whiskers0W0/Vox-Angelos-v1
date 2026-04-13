using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public List<ConcernViewModel> Concerns { get; set; } = new();
        public string CurrentFilter { get; set; } = "Unresolved";

        public async Task OnGetAsync(string? filter)
        {
            CurrentFilter = filter ?? "Unresolved";

            var query = _db.Concerns
                .Include(c => c.Attachments)
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .AsQueryable();

            if (CurrentFilter != "All")
            {
                query = query.Where(c => c.Status == CurrentFilter);
            }

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
                    Status = c.Status,
                    LocationName = c.LocationName ?? "No location provided",
                    Latitude = c.Latitude,
                    Longitude = c.Longitude,
                    SubmittedAt = c.SubmittedAt,
                    FirstAttachmentPath = c.Attachments
                        .Where(a => a.FileType == "image")
                        .Select(a => a.FilePath)
                        .FirstOrDefault()
                })
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(
            int concernId, string status, string? notes)
        {
            var concern = await _db.Concerns.FindAsync(concernId);
            if (concern == null) return NotFound();

            concern.Status = status;
            concern.LguNotes = notes;
            concern.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostChooseConcernAsync(int concernId)
        {
            var concern = await _db.Concerns.FindAsync(concernId);
            if (concern == null) return NotFound();

            concern.Status = "Chosen";
            concern.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage(new { filter = "Chosen" });
        }
    }

    public class ConcernViewModel
    {
        public int Id { get; set; }
        public string CitizenName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string? FirstAttachmentPath { get; set; }
    }
}