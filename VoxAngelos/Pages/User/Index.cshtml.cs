using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.User
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RecommendationRatingService _ratingService;
        private readonly IHubContext<FeedHub> _feedHub;

        public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            RecommendationRatingService ratingService, IHubContext<FeedHub> feedHub)
        {
            _db = db;
            _userManager = userManager;
            _ratingService = ratingService;
            _feedHub = feedHub;
        }

        public List<RecommendationCardViewModel> Recommendations { get; set; } = new();
        public List<RecommendationCardViewModel> TopRated { get; set; } = new();
        public string CurrentUserId { get; set; } = string.Empty;

        // The 7 LGU department codes concerns/recommendations are routed to
        // (see ConcernClassificationService) — used to populate the office filter dropdown.
        public static readonly List<Office> Offices = new()
            {
                new Office
                {
                    Code = "SWDO",
                    Name = "Social Welfare and Development Office"
                },
                new Office
                {
                    Code = "CEO",
                    Name = "City Engineer's Office"
                },
                new Office
                {
                    Code = "CENRO",
                    Name = "City Environment and Natural Resources Office"
                },
                new Office
                {
                    Code = "ACDO",
                    Name = "Agricultural and Cooperative Development Office"
                },
                new Office
                {
                    Code = "PTRO",
                    Name = "Public Transport and Traffic Regulation Office"
                },
                new Office
                {
                    Code = "OSCA",
                    Name = "Office of the Senior Citizens Affairs"
                },
                new Office
                {
                    Code = "PWDAO",
                    Name = "Persons with Disability Affairs Office"
                }
            };

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
            public int MyUrgency { get; set; }
            public int MyRelevance { get; set; }
            public int MyFeasibility { get; set; }
            public List<string> AttachmentPaths { get; set; } = new();
            public List<string> AttachmentTypes { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            CurrentUserId = user?.Id ?? string.Empty;

            var recs = await _db.Recommendations
                .Where(r => r.Status == "Published")
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .OrderByDescending(r => r.ReviewedAt)
                .ToListAsync();

            var myRatings = await _ratingService.GetMyRatingsAsync(CurrentUserId);

            Recommendations = recs.Select(r => MapToViewModel(r, myRatings)).ToList();

            var topRated = await _ratingService.GetTopRecommendationsAsync(forLgu: false);
            TopRated = topRated.Select(r => MapToViewModel(r, myRatings)).ToList();
        }

        private static RecommendationCardViewModel MapToViewModel(
            Recommendation r, Dictionary<int, RecommendationRating> myRatings)
        {
            myRatings.TryGetValue(r.Id, out var mine);

            return new RecommendationCardViewModel
            {
                Id = r.Id,
                CitizenName = r.IsAnonymous
                    ? "Anonymous Citizen"
                    : r.Citizen.UserProfile != null
                        ? $"{r.Citizen.UserProfile.FirstName} {r.Citizen.UserProfile.LastName}"
                        : r.Citizen.Email ?? "Citizen",
                Category = r.Category,
                AssignedOffice = r.AssignedOffice ?? string.Empty,
                Title = r.Title,
                Description = r.Description,
                ApprovedAt = r.ReviewedAt ?? r.SubmittedAt,
                RatingCount = r.RatingCount,
                AvgUrgency = r.AvgUrgency,
                AvgRelevance = r.AvgRelevance,
                AvgFeasibility = r.AvgFeasibility,
                CompositeScore = r.CompositeScore,
                MyUrgency = mine?.UrgencyStars ?? 0,
                MyRelevance = mine?.RelevanceStars ?? 0,
                MyFeasibility = mine?.FeasibilityStars ?? 0,
                AttachmentPaths = r.Attachments.Select(a => a.FilePath).ToList(),
                AttachmentTypes = r.Attachments.Select(a => a.FileType).ToList()
            };
        }

        public async Task<IActionResult> OnPostRateAsync(
            int recommendationId, int urgencyStars, int relevanceStars, int feasibilityStars)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            try
            {
                await _ratingService.SubmitRatingAsync(
                    recommendationId, user.Id, urgencyStars, relevanceStars, feasibilityStars);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(ex.Message);
            }

            var rec = await _db.Recommendations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == recommendationId);
            if (rec == null) return NotFound();

            var payload = new
            {
                recommendationId,
                ratingCount = rec.RatingCount,
                avgUrgency = Math.Round(rec.AvgUrgency, 1),
                avgRelevance = Math.Round(rec.AvgRelevance, 1),
                avgFeasibility = Math.Round(rec.AvgFeasibility, 1),
                compositeScore = Math.Round(rec.CompositeScore, 1)
            };

            // Push the new aggregate to every other citizen currently viewing the
            // Discover feed, so their star averages update with no page refresh.
            await _feedHub.Clients.Group(FeedHub.DiscoverGroup).SendAsync("RatingUpdated", payload);

            return new JsonResult(new
            {
                success = true,
                ratingCount = rec.RatingCount,
                avgUrgency = Math.Round(rec.AvgUrgency, 1),
                avgRelevance = Math.Round(rec.AvgRelevance, 1),
                avgFeasibility = Math.Round(rec.AvgFeasibility, 1),
                compositeScore = Math.Round(rec.CompositeScore, 1),
                myUrgency = urgencyStars,
                myRelevance = relevanceStars,
                myFeasibility = feasibilityStars
            });
        }
    }
}
