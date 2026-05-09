using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.User
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public List<RecommendationCardViewModel> Recommendations { get; set; } = new();
        public List<RecommendationCardViewModel> TopVotes { get; set; } = new();
        public string CurrentUserId { get; set; } = string.Empty;

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
            public string? CurrentUserVote { get; set; } // "up", "down", or null
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
                .Include(r => r.Votes)
                .OrderByDescending(r => r.ReviewedAt)
                .ToListAsync();

            var userVotes = await _db.RecommendationVotes
                .Where(v => v.CitizenId == CurrentUserId)
                .ToDictionaryAsync(v => v.RecommendationId, v => v.VoteType);

            Recommendations = recs.Select(r => MapToViewModel(r, userVotes)).ToList();

            TopVotes = recs
                .OrderByDescending(r => r.Upvotes)
                .Take(10)
                .Select(r => MapToViewModel(r, userVotes))
                .ToList();
        }

        private RecommendationCardViewModel MapToViewModel(
            Recommendation r,
            Dictionary<int, string> userVotes)
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
                CurrentUserVote = userVotes.TryGetValue(r.Id, out var v) ? v : null,
                AttachmentPaths = r.Attachments.Select(a => a.FilePath).ToList(),
                AttachmentTypes = r.Attachments.Select(a => a.FileType).ToList()
            };
        }

        public async Task<IActionResult> OnPostVoteAsync(int recommendationId, string voteType)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var rec = await _db.Recommendations.FindAsync(recommendationId);
            if (rec == null) return NotFound();

            var existing = await _db.RecommendationVotes
                .FirstOrDefaultAsync(v => v.RecommendationId == recommendationId && v.CitizenId == user.Id);

            string? finalVote = null;

            if (existing != null)
            {
                if (existing.VoteType == voteType)
                {
                    _db.RecommendationVotes.Remove(existing);
                    if (voteType == "up") rec.Upvotes = Math.Max(0, rec.Upvotes - 1);
                    else rec.Downvotes = Math.Max(0, rec.Downvotes - 1);
                    finalVote = null;
                }
                else
                {
                    if (existing.VoteType == "up") { rec.Upvotes = Math.Max(0, rec.Upvotes - 1); rec.Downvotes++; }
                    else { rec.Downvotes = Math.Max(0, rec.Downvotes - 1); rec.Upvotes++; }
                    existing.VoteType = voteType;
                    finalVote = voteType;
                }
            }
            else
            {
                _db.RecommendationVotes.Add(new RecommendationVote
                {
                    RecommendationId = recommendationId,
                    CitizenId = user.Id,
                    VoteType = voteType
                });
                if (voteType == "up") rec.Upvotes++;
                else rec.Downvotes++;
                finalVote = voteType;
            }

            await _db.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                upvotes = rec.Upvotes,
                downvotes = rec.Downvotes,
                userVote = finalVote
            });
        }
    }
}