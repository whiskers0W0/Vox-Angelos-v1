using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class LeaderboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly RecommendationRatingService _ratingService;

        public LeaderboardModel(ApplicationDbContext db, RecommendationRatingService ratingService)
        {
            _db = db;
            _ratingService = ratingService;
        }

        public List<RecommendationCardViewModel> TopRated { get; set; } = new();
        public List<RecommendationCardViewModel> AllRecommendations { get; set; } = new();

        public class RecommendationCardViewModel
        {
            public int Id { get; set; }
            public string CitizenName { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string AssignedOffice { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime ApprovedAt { get; set; }
            public int RatingCount { get; set; }
            public double AvgUrgency { get; set; }
            public double AvgRelevance { get; set; }
            public double AvgFeasibility { get; set; }
            public double CompositeScore { get; set; }
            public List<string> AttachmentPaths { get; set; } = new();
            public List<string> AttachmentTypes { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            var recs = await _db.Recommendations
                .Where(r => r.Status == "Published")
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .OrderByDescending(r => r.ReviewedAt)
                .ToListAsync();

            AllRecommendations = recs.Select(Map).ToList();

            var topRated = await _ratingService.GetTopRecommendationsAsync(forLgu: true);
            TopRated = topRated.Select(Map).ToList();
        }

        private static RecommendationCardViewModel Map(Recommendation r)
        {
            return new RecommendationCardViewModel
            {
                Id = r.Id,
                CitizenName = r.Citizen.UserProfile != null
                    ? $"{r.Citizen.UserProfile.FirstName} {r.Citizen.UserProfile.LastName}"
                    : r.Citizen.Email ?? "Citizen",
                Category = r.Category,
                AssignedOffice = string.Empty,
                Title = r.Title,
                Description = r.Description,
                ApprovedAt = r.ReviewedAt ?? r.SubmittedAt,
                RatingCount = r.RatingCount,
                AvgUrgency = r.AvgUrgency,
                AvgRelevance = r.AvgRelevance,
                AvgFeasibility = r.AvgFeasibility,
                CompositeScore = r.CompositeScore,
                AttachmentPaths = r.Attachments.Select(a => a.FilePath).ToList(),
                AttachmentTypes = r.Attachments.Select(a => a.FileType).ToList()
            };
        }
    }
}
