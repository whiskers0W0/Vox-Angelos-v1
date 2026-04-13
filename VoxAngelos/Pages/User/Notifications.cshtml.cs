using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class NotificationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public string CitizenFullName { get; set; } = string.Empty;
        public List<ConcernNotificationViewModel> Outbox { get; set; } = new();
        public List<ConcernNotificationViewModel> Inbox { get; set; } = new();

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            var profile = await _db.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            CitizenFullName = profile != null
                ? $"{profile.FirstName} {profile.LastName}"
                : user.Email ?? "Citizen";

            var allConcerns = await _db.Concerns
                .Include(c => c.Attachments)
                .Where(c => c.CitizenId == user.Id)
                .OrderByDescending(c => c.SubmittedAt)
                .Select(c => new ConcernNotificationViewModel
                {
                    Id = c.Id,
                    Description = c.Description,
                    Category = c.Category ?? "Uncategorized",
                    Status = c.Status,
                    LocationName = c.LocationName ?? "No location provided",
                    SubmittedAt = c.SubmittedAt,
                    UpdatedAt = c.UpdatedAt,
                    LguNotes = c.LguNotes,
                    FirstAttachmentPath = c.Attachments
                        .Where(a => a.FileType == "image")
                        .Select(a => a.FilePath)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Outbox — no LGU action yet
            Outbox = allConcerns
                .Where(c => c.Status == "Unresolved")
                .ToList();

            // Inbox — any LGU action has occurred
            Inbox = allConcerns
                .Where(c => c.Status != "Unresolved")
                .ToList();
        }

        public string GetTimeAgo(DateTime dt)
        {
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }

    }

    public class ConcernNotificationViewModel
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? LguNotes { get; set; }
        public string? FirstAttachmentPath { get; set; }
    }
}