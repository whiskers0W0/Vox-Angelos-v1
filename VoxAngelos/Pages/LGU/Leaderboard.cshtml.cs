using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class LeaderboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public LeaderboardModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public List<RecommendationCardViewModel> TopVotes { get; set; } = new();
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
            public int Upvotes { get; set; }
            public int Downvotes { get; set; }
            public List<string> AttachmentPaths { get; set; } = new();
            public List<string> AttachmentTypes { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            var recs = await _db.Recommendations
                .Where(r => r.Status == "Approved")
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .OrderByDescending(r => r.ReviewedAt)
                .ToListAsync();

            AllRecommendations = recs.Select(Map).ToList();

            TopVotes = recs
                .OrderByDescending(r => r.Upvotes)
                .Take(10)
                .Select(Map)
                .ToList();
        }

        private RecommendationCardViewModel Map(Recommendation r)
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
                Upvotes = r.Upvotes,
                Downvotes = r.Downvotes,
                AttachmentPaths = r.Attachments.Select(a => a.FilePath).ToList(),
                AttachmentTypes = r.Attachments.Select(a => a.FileType).ToList()
            };
        }
    }
}