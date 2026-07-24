using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class RecommendationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public RecommendationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public string CitizenFullName { get; private set; } = "Citizen";
        public string CurrentStatusFilter { get; private set; } = "All";
        public List<RecommendationItemViewModel> Recommendations { get; private set; } = new();

        public async Task OnGetAsync(string status = "All")
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            CurrentStatusFilter = status is "All" or "Pending" or "Published" or "Rejected"
                ? status
                : "All";

            var profile = await _db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);
            CitizenFullName = profile == null
                ? user.Email ?? "Citizen"
                : string.Join(" ", new[] { profile.FirstName, profile.LastName }
                    .Where(name => !string.IsNullOrWhiteSpace(name)));

            var query = _db.Recommendations.AsNoTracking()
                .Include(r => r.Attachments)
                .Where(r => r.CitizenId == user.Id && r.Status != "Draft");

            if (CurrentStatusFilter != "All")
                query = query.Where(r => r.Status == CurrentStatusFilter);

            var records = await query
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            Recommendations = records.Select(MapRecommendation).ToList();
        }

        public string GetTimeAgo(DateTime dateTime)
        {
            var elapsed = DateTime.UtcNow - dateTime;
            if (elapsed.TotalSeconds < 60) return $"{Math.Max(1, (int)elapsed.TotalSeconds)}s ago";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            return $"{(int)elapsed.TotalDays}d ago";
        }

        private RecommendationItemViewModel MapRecommendation(Recommendation recommendation)
        {
            var item = new RecommendationItemViewModel
            {
                Id = recommendation.Id,
                Title = recommendation.Title,
                Description = recommendation.Description,
                Category = recommendation.Category,
                AssignedOffice = recommendation.AssignedOffice,
                Location = recommendation.Location,
                Status = recommendation.Status,
                LguNotes = recommendation.LguNotes,
                SubmittedAt = recommendation.SubmittedAt,
                ReviewedAt = recommendation.ReviewedAt,
                RatingCount = recommendation.RatingCount,
                CompositeScore = recommendation.CompositeScore,
                AvgUrgency = recommendation.AvgUrgency,
                AvgRelevance = recommendation.AvgRelevance,
                AvgFeasibility = recommendation.AvgFeasibility,
                Attachments = recommendation.Attachments
                    .OrderBy(a => a.UploadedAt)
                    .Select(a => new RecommendationAttachmentViewModel
                    {
                        FilePath = a.FilePath,
                        FileType = a.FileType
                    })
                    .ToList()
            };

            item.Timeline.Add(new RecommendationTimelineItemViewModel
            {
                EventType = item.Status == "Draft" ? "Draft saved" : "Submitted",
                Status = item.Status == "Draft" ? "Draft" : "Pending",
                Message = item.Status == "Draft"
                    ? "This recommendation is saved as a draft and has not been sent to the LGU."
                    : "Your recommendation was submitted and is awaiting LGU review.",
                ActorName = CitizenFullName,
                CreatedAt = item.SubmittedAt
            });

            if (item.ReviewedAt.HasValue)
            {
                item.Timeline.Add(new RecommendationTimelineItemViewModel
                {
                    EventType = "LGU review",
                    Status = item.Status,
                    Message = string.IsNullOrWhiteSpace(item.LguNotes)
                        ? $"Your recommendation was {item.Status.ToLowerInvariant()}."
                        : item.LguNotes,
                    ActorName = item.AssignedOffice ?? "LGU Office",
                    CreatedAt = item.ReviewedAt.Value
                });
            }

            return item;
        }
    }

    public class RecommendationItemViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? AssignedOffice { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? LguNotes { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int RatingCount { get; set; }
        public double CompositeScore { get; set; }
        public double AvgUrgency { get; set; }
        public double AvgRelevance { get; set; }
        public double AvgFeasibility { get; set; }
        public List<RecommendationAttachmentViewModel> Attachments { get; set; } = new();
        public List<RecommendationTimelineItemViewModel> Timeline { get; set; } = new();
    }

    public class RecommendationAttachmentViewModel
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }

    public class RecommendationTimelineItemViewModel
    {
        public string EventType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
