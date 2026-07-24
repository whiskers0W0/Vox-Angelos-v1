using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Hubs;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class ReviewRecommendationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<FeedHub> _feedHub;

        public ReviewRecommendationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IHubContext<FeedHub> feedHub)
        {
            _db = db;
            _userManager = userManager;
            _feedHub = feedHub;
        }

        public List<RecommendationViewModel> Recommendations { get; set; } = new();
        public string CurrentFilter { get; set; } = "Pending";

        public class RecommendationViewModel
        {
            public int Id { get; set; }
            public string CitizenName { get; set; } = string.Empty;
            public string Justification { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string? AssignedOffice { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Beneficiaries { get; set; } = string.Empty;
            public int EstimatedPeopleAffected { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? LguNotes { get; set; }
            public DateTime SubmittedAt { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public List<string> AttachmentPaths { get; set; } = new();
            public List<string> AttachmentTypes { get; set; } = new();
        }

        public async Task OnGetAsync(string filter = "Pending")
        {
            CurrentFilter = filter;

            var user = await _userManager.GetUserAsync(User);
            var userDepartment = user?.Department;

            var query = _db.Recommendations
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .Where(r => r.Status != "Draft")
                .AsQueryable();

            // Show recommendations whose classified office matches this LGU's department
            if (!string.IsNullOrEmpty(userDepartment))
            {
                query = query.Where(r => r.AssignedOffice == userDepartment || r.AssignedOffice == null);
            }

            if (filter != "All")
                query = query.Where(r => r.Status == filter);

            var recs = await query
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            Recommendations = recs.Select(r => new RecommendationViewModel
            {
                Id = r.Id,
                CitizenName = r.Citizen.UserProfile != null
                    ? $"{r.Citizen.UserProfile.FirstName} {r.Citizen.UserProfile.LastName}"
                    : r.Citizen.Email ?? "Citizen",
                Justification = r.Justification,
                Category = r.Category,
                AssignedOffice = r.AssignedOffice,
                Title = r.Title,
                Location = r.Location,
                Description = r.Description,
                Beneficiaries = r.Beneficiaries,
                EstimatedPeopleAffected = r.EstimatedPeopleAffected,
                Status = r.Status,
                LguNotes = r.LguNotes,
                SubmittedAt = r.SubmittedAt,
                ReviewedAt = r.ReviewedAt,
                AttachmentPaths = r.Attachments.Select(a => a.FilePath).ToList(),
                AttachmentTypes = r.Attachments.Select(a => a.FileType).ToList()
            }).ToList();
        }

        public async Task<IActionResult> OnPostApproveAsync(int recommendationId, string? lguNotes)
        {
            var user = await _userManager.GetUserAsync(User);
            var reviewedAt = DateTime.UtcNow;

            // Guarded by current Status so two staff reviewing the same recommendation
            // at once can't both "win" — only the first review is applied.
            var updated = await _db.Recommendations
                .Where(r => r.Id == recommendationId && r.Status == "Pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "Published")
                    .SetProperty(r => r.LguNotes, lguNotes)
                    .SetProperty(r => r.ReviewedByLguId, user!.Id)
                    .SetProperty(r => r.ReviewedAt, reviewedAt));

            if (updated == 0)
                TempData["RecError"] = "This recommendation was already reviewed by another staff member.";
            else
            {
                var recommendation = await _db.Recommendations
                    .Where(r => r.Id == recommendationId)
                    .Select(r => new { r.CitizenId })
                    .SingleAsync();
                var actorName = user?.Department ?? user?.Email ?? "LGU Office";
                var notificationMessage = string.IsNullOrWhiteSpace(lguNotes)
                    ? "Your recommendation has been published."
                    : lguNotes;

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recommendation.CitizenId,
                    Title = "Your recommendation was published",
                    Message = notificationMessage,
                    NotificationType = "RecommendationUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Recommendations",
                    CreatedAt = reviewedAt
                });
                await _db.SaveChangesAsync();

                await _feedHub.Clients.Group(FeedHub.DiscoverGroup).SendAsync("PostPublished");
            }

            return RedirectToPage(new { filter = "Pending" });
        }

        public async Task<IActionResult> OnPostRejectAsync(int recommendationId, string? lguNotes)
        {
            var user = await _userManager.GetUserAsync(User);
            var reviewedAt = DateTime.UtcNow;

            var updated = await _db.Recommendations
                .Where(r => r.Id == recommendationId && r.Status == "Pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "Rejected")
                    .SetProperty(r => r.LguNotes, lguNotes)
                    .SetProperty(r => r.ReviewedByLguId, user!.Id)
                    .SetProperty(r => r.ReviewedAt, reviewedAt));

            if (updated == 0)
                TempData["RecError"] = "This recommendation was already reviewed by another staff member.";
            else
            {
                var recommendation = await _db.Recommendations
                    .Where(r => r.Id == recommendationId)
                    .Select(r => new { r.CitizenId })
                    .SingleAsync();
                var actorName = user?.Department ?? user?.Email ?? "LGU Office";
                var notificationMessage = string.IsNullOrWhiteSpace(lguNotes)
                    ? "Your recommendation was not published."
                    : lguNotes;

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recommendation.CitizenId,
                    Title = "Your recommendation was not published",
                    Message = notificationMessage,
                    NotificationType = "RecommendationUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Recommendations",
                    CreatedAt = reviewedAt
                });
                await _db.SaveChangesAsync();
            }

            return RedirectToPage(new { filter = "Pending" });
        }
    }
}
