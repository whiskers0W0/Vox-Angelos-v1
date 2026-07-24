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
        public string CurrentStatusFilter { get; private set; } = "All";
        public List<ConcernNotificationViewModel> Outbox { get; set; } = new();

        public async Task OnGetAsync(string status = "All")
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            CurrentStatusFilter = status is "All" or "Unresolved" or "Chosen" or "In Progress" or "Resolved"
                ? status
                : "All";

            var profile = await _db.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            CitizenFullName = profile != null
                ? $"{profile.FirstName} {profile.LastName}"
                : user.Email ?? "Citizen";

            var concernsQuery = _db.Concerns
                .Include(c => c.Attachments)
                .Where(c => c.CitizenId == user.Id && c.Status != "Draft");

            if (CurrentStatusFilter != "All")
            {
                concernsQuery = concernsQuery.Where(c => c.Status == CurrentStatusFilter);
            }

            var allConcerns = await concernsQuery
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
                    Attachments = c.Attachments
                        .OrderBy(a => a.UploadedAt)
                        .Select(a => new ConcernAttachmentViewModel
                        {
                            FilePath = a.FilePath,
                            // Older concern uploads stored PDFs as "image". Identify
                            // them from the saved file extension so existing records
                            // render as documents too.
                            FileType = a.FilePath.EndsWith(".pdf") ? "document" : a.FileType
                        })
                        .ToList(),
                    Timeline = c.TimelineEvents
                        .OrderBy(e => e.CreatedAt)
                        .Select(e => new ConcernTimelineItemViewModel
                        {
                            EventType = e.EventType,
                            Status = e.Status,
                            Message = e.Message,
                            ActorRole = e.ActorRole,
                            ActorName = e.ActorName,
                            CreatedAt = e.CreatedAt
                        })
                        .ToList()
                })
                .ToListAsync();

            foreach (var concern in allConcerns)
            {
                var hasSavedTimeline = concern.Timeline.Any();

                if (!concern.Timeline.Any(e => e.EventType == "Submitted"))
                {
                    concern.Timeline.Add(new ConcernTimelineItemViewModel
                    {
                        EventType = "Submitted",
                        Status = "Unresolved",
                        Message = "Your concern was submitted.",
                        ActorRole = "Citizen",
                        ActorName = CitizenFullName,
                        CreatedAt = concern.SubmittedAt
                    });
                }

                if (!hasSavedTimeline && concern.UpdatedAt.HasValue)
                {
                    concern.Timeline.Add(new ConcernTimelineItemViewModel
                    {
                        EventType = "Status Updated",
                        Status = concern.Status,
                        Message = string.IsNullOrWhiteSpace(concern.LguNotes)
                            ? $"The concern status is {concern.Status}."
                            : concern.LguNotes,
                        ActorRole = "LGU",
                        ActorName = "LGU Office",
                        CreatedAt = concern.UpdatedAt.Value
                    });
                }

                concern.Timeline = concern.Timeline
                    .OrderBy(e => e.CreatedAt)
                    .ToList();
            }

            // Outbox — no LGU action yet
            Outbox = allConcerns;
        }

        public async Task<IActionResult> OnPostMarkNotificationReadAsync(int notificationId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == user.Id);

            if (notification == null) return NotFound();

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
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
        public List<ConcernAttachmentViewModel> Attachments { get; set; } = new();
        public List<ConcernTimelineItemViewModel> Timeline { get; set; } = new();
    }

    public class ConcernAttachmentViewModel
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }

    public class ConcernTimelineItemViewModel
    {
        public string EventType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string ActorRole { get; set; } = string.Empty;
        public string? ActorName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
