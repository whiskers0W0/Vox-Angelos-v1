using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class DiscoverModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly RecommendationRatingService _ratingService;
        private readonly IWebHostEnvironment _environment;

        public DiscoverModel(
            ApplicationDbContext db,
            RecommendationRatingService ratingService,
            IWebHostEnvironment environment)
        {
            _db = db;
            _ratingService = ratingService;
            _environment = environment;
        }

        public List<RecommendationCardViewModel> Recommendations { get; set; } = new();
        public List<RecommendationCardViewModel> TopRated { get; set; } = new();

        public async Task OnGetAsync()
        {
            var recommendations = await _db.Recommendations
                .AsNoTracking()
                .Where(recommendation => recommendation.Status == "Published")
                .Include(recommendation => recommendation.Citizen)
                    .ThenInclude(citizen => citizen.UserProfile)
                .Include(recommendation => recommendation.Attachments)
                .OrderByDescending(recommendation => recommendation.ReviewedAt)
                .ToListAsync();

            Recommendations = recommendations.Select(Map).ToList();

            var topRated = await _ratingService.GetTopRecommendationsAsync(forLgu: true);
            TopRated = topRated.Select(Map).ToList();
        }

        private RecommendationCardViewModel Map(Recommendation recommendation)
        {
            var attachments = recommendation.Attachments
                .Where(attachment => IsAvailableAttachmentPath(attachment.FilePath))
                .ToList();

            return new RecommendationCardViewModel
            {
                Id = recommendation.Id,
                CitizenName = recommendation.IsAnonymous
                    ? "Anonymous Citizen"
                    : recommendation.Citizen.UserProfile != null
                        ? $"{recommendation.Citizen.UserProfile.FirstName} {recommendation.Citizen.UserProfile.LastName}"
                        : recommendation.Citizen.Email ?? "Citizen",
                Category = recommendation.Category ?? string.Empty,
                AssignedOffice = recommendation.AssignedOffice ?? string.Empty,
                Title = recommendation.Title,
                Description = recommendation.Description,
                ApprovedAt = recommendation.ReviewedAt ?? recommendation.SubmittedAt,
                RatingCount = recommendation.RatingCount,
                AvgUrgency = recommendation.AvgUrgency,
                AvgRelevance = recommendation.AvgRelevance,
                AvgFeasibility = recommendation.AvgFeasibility,
                CompositeScore = recommendation.CompositeScore,
                AttachmentPaths = attachments.Select(attachment => attachment.FilePath).ToList(),
                AttachmentTypes = attachments.Select(attachment => attachment.FileType).ToList()
            };
        }

        private bool IsAvailableAttachmentPath(string? attachmentPath)
        {
            if (string.IsNullOrWhiteSpace(attachmentPath))
                return false;

            if (Uri.TryCreate(attachmentPath, UriKind.Absolute, out var remoteUri) &&
                (remoteUri.Scheme == Uri.UriSchemeHttps || remoteUri.Scheme == Uri.UriSchemeHttp))
                return true;

            var relativePath = attachmentPath
                .TrimStart('~', '/', '\\')
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var webRoot = Path.GetFullPath(_environment.WebRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var requiredPrefix = webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(fullPath);
        }

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
    }
}
