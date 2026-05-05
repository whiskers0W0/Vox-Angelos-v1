using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class ReviewRecommendationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewRecommendationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public List<RecommendationViewModel> Recommendations { get; set; } = new();
        public string CurrentFilter { get; set; } = "Pending";

        public class RecommendationViewModel
        {
            public int Id { get; set; }
            public string CitizenName { get; set; } = string.Empty;
            public string Justification { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
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

            var query = _db.Recommendations
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .AsQueryable();

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
            var rec = await _db.Recommendations.FindAsync(recommendationId);
            if (rec == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            rec.Status = "Approved";
            rec.LguNotes = lguNotes;
            rec.ReviewedByLguId = user?.Id;
            rec.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage(new { filter = "Pending" });
        }

        public async Task<IActionResult> OnPostRejectAsync(int recommendationId, string? lguNotes)
        {
            var rec = await _db.Recommendations.FindAsync(recommendationId);
            if (rec == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            rec.Status = "Rejected";
            rec.LguNotes = lguNotes;
            rec.ReviewedByLguId = user?.Id;
            rec.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage(new { filter = "Pending" });
        }
    }
}