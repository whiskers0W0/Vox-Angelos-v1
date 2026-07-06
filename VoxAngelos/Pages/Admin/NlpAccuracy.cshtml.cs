using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class NlpAccuracyModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public const int TargetAccuracyPercent = 75;

        public NlpAccuracyModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public int TotalReviewed { get; set; }
        public int CorrectCount { get; set; }
        public int IncorrectCount { get; set; }
        public double AccuracyPercent { get; set; }
        public bool ThresholdMet { get; set; }

        public List<DepartmentAccuracyViewModel> ByDepartment { get; set; } = new();
        public List<MisclassificationViewModel> RecentMisclassifications { get; set; } = new();

        public async Task OnGetAsync()
        {
            var corrections = await _db.ClassificationCorrections
                .Include(cc => cc.Concern)
                .Include(cc => cc.ReviewedBy)
                .OrderByDescending(cc => cc.ReviewedAt)
                .ToListAsync();

            TotalReviewed = corrections.Count;
            CorrectCount = corrections.Count(c => c.WasCorrect);
            IncorrectCount = TotalReviewed - CorrectCount;
            AccuracyPercent = TotalReviewed == 0 ? 0 : Math.Round(CorrectCount * 100.0 / TotalReviewed, 1);
            ThresholdMet = AccuracyPercent >= TargetAccuracyPercent;

            ByDepartment = corrections
                .GroupBy(c => c.CorrectedCategory)
                .Select(g => new DepartmentAccuracyViewModel
                {
                    Department = g.Key,
                    Total = g.Count(),
                    Correct = g.Count(c => c.WasCorrect),
                    AccuracyPercent = Math.Round(g.Count(c => c.WasCorrect) * 100.0 / g.Count(), 1)
                })
                .OrderBy(d => d.Department)
                .ToList();

            RecentMisclassifications = corrections
                .Where(c => !c.WasCorrect)
                .Take(15)
                .Select(c => new MisclassificationViewModel
                {
                    ConcernId = c.ConcernId,
                    Description = c.Concern.Description,
                    GuessedCategory = c.PreviousCategory ?? "Uncategorized",
                    ActualCategory = c.CorrectedCategory,
                    ReviewedByEmail = c.ReviewedBy.Email ?? "N/A",
                    ReviewedAt = c.ReviewedAt
                })
                .ToList();
        }
    }

    public class DepartmentAccuracyViewModel
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Correct { get; set; }
        public double AccuracyPercent { get; set; }
    }

    public class MisclassificationViewModel
    {
        public int ConcernId { get; set; }
        public string Description { get; set; } = string.Empty;
        public string GuessedCategory { get; set; } = string.Empty;
        public string ActualCategory { get; set; } = string.Empty;
        public string ReviewedByEmail { get; set; } = string.Empty;
        public DateTime ReviewedAt { get; set; }
    }
}
