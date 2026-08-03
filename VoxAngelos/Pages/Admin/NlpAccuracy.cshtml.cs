using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class NlpAccuracyModel : PageModel
    {
        // Still used by the Admin dashboard's performance summary; the target card was
        // removed from this page, but the underlying reporting threshold remains intact.
        public const int TargetAccuracyPercent = 75;

        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;

        public NlpAccuracyModel(ApplicationDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        public bool AdminTriageEnabled =>
            _configuration.GetValue<bool>("SubmissionRouting:AdminTriageUncategorized");

        public string[] Departments => ConcernClassificationService.Departments;
        public List<DepartmentAccuracyViewModel> ByDepartment { get; set; } = new();
        public List<UncategorizedSubmissionViewModel> UncategorizedSubmissions { get; set; } = new();
        public List<MisclassificationViewModel> RecentMisclassifications { get; set; } = new();
        public string CurrentType { get; set; } = "All";
        public string CurrentSort { get; set; } = "Oldest";

        public async Task OnGetAsync(string? submissionType, string? sortOrder)
        {
            CurrentType = submissionType is "Concern" or "Recommendation" ? submissionType : "All";
            CurrentSort = string.Equals(sortOrder, "Newest", StringComparison.OrdinalIgnoreCase)
                ? "Newest"
                : "Oldest";

            var corrections = await _db.ClassificationCorrections
                .Include(correction => correction.Concern)
                .Include(correction => correction.ReviewedBy)
                .OrderByDescending(correction => correction.ReviewedAt)
                .ToListAsync();

            ByDepartment = corrections
                .GroupBy(correction => correction.CorrectedCategory)
                .Select(group => new DepartmentAccuracyViewModel
                {
                    Department = group.Key,
                    Total = group.Count(),
                    Correct = group.Count(correction => correction.WasCorrect),
                    AccuracyPercent = Math.Round(group.Count(correction => correction.WasCorrect) * 100.0 / group.Count(), 1)
                })
                .OrderBy(department => department.Department)
                .ToList();

            if (AdminTriageEnabled)
            {
                await LoadUncategorizedSubmissionsAsync();
                return;
            }

            RecentMisclassifications = corrections
                .Where(correction => !correction.WasCorrect)
                .Take(15)
                .Select(correction => new MisclassificationViewModel
                {
                    ConcernId = correction.ConcernId,
                    Description = correction.Concern.Description,
                    GuessedCategory = correction.PreviousCategory ?? "Uncategorized",
                    ActualCategory = correction.CorrectedCategory,
                    ReviewedByEmail = correction.ReviewedBy.Email ?? "N/A",
                    ReviewedAt = correction.ReviewedAt
                })
                .ToList();
        }

        public async Task<IActionResult> OnPostAssignAsync(
            string submissionType,
            int submissionId,
            string department,
            string? currentType,
            string? currentSort)
        {
            if (!AdminTriageEnabled)
                return NotFound();

            if (!Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
            {
                TempData["RoutingError"] = "Select a valid LGU department.";
                return RedirectToPage(new { submissionType = currentType, sortOrder = currentSort });
            }

            var normalizedDepartment = Departments.Single(item =>
                item.Equals(department, StringComparison.OrdinalIgnoreCase));
            var isConcern = string.Equals(submissionType, "Concern", StringComparison.OrdinalIgnoreCase);
            var isRecommendation = string.Equals(submissionType, "Recommendation", StringComparison.OrdinalIgnoreCase);

            if (!isConcern && !isRecommendation)
                return BadRequest();

            var updated = isConcern
                ? await _db.Concerns
                    .Where(concern => concern.Id == submissionId &&
                        concern.Status == "Unresolved" &&
                        (concern.Category == null || concern.Category == ""))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(concern => concern.Category, normalizedDepartment)
                        .SetProperty(concern => concern.AssignedOffice, normalizedDepartment))
                : await _db.Recommendations
                    .Where(recommendation => recommendation.Id == submissionId &&
                        recommendation.Status == "Pending" &&
                        (recommendation.AssignedOffice == null || recommendation.AssignedOffice == ""))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(recommendation => recommendation.AssignedOffice, normalizedDepartment));

            if (updated == 0)
            {
                TempData["RoutingError"] = "This submission was already assigned or is no longer available.";
                return RedirectToPage(new { submissionType = currentType, sortOrder = currentSort });
            }

            var recipientIds = await _db.Users
                .AsNoTracking()
                .Where(user => user.Department == normalizedDepartment &&
                    (user.LockoutEnd == null || user.LockoutEnd < DateTimeOffset.UtcNow))
                .Select(user => user.Id)
                .ToListAsync();

            foreach (var recipientId in recipientIds)
            {
                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = recipientId,
                    Title = isConcern ? "Concern assigned by Admin" : "Recommendation assigned by Admin",
                    Message = $"Admin routed an uncategorized {submissionType.ToLowerInvariant()} to your office for review.",
                    NotificationType = isConcern ? "IncomingConcern" : "IncomingRecommendation",
                    SenderRole = "Admin",
                    SenderName = "Vox Angelos Admin",
                    LinkUrl = isConcern ? "/LGU/Index?filter=Unresolved" : "/LGU/ReviewRecommendations?filter=Pending",
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (isConcern)
            {
                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = submissionId,
                    EventType = "Routed",
                    Status = "Unresolved",
                    Message = $"Admin routed this concern to {normalizedDepartment} for review.",
                    ActorRole = "Admin",
                    ActorName = User.Identity?.Name ?? "Vox Angelos Admin",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            TempData["RoutingSuccess"] = $"The {submissionType.ToLowerInvariant()} was assigned to {normalizedDepartment}.";
            return RedirectToPage(new { submissionType = currentType, sortOrder = currentSort });
        }

        private async Task LoadUncategorizedSubmissionsAsync()
        {
            var concernEntities = await _db.Concerns
                .AsNoTracking()
                .Include(concern => concern.Attachments)
                .Where(concern => concern.Status == "Unresolved" &&
                    (concern.Category == null || concern.Category == ""))
                .ToListAsync();

            var concerns = concernEntities
                .Select(concern => new UncategorizedSubmissionViewModel
                {
                    Id = concern.Id,
                    Type = "Concern",
                    Summary = concern.Description,
                    Description = concern.Description,
                    Location = concern.LocationName ?? "No location provided",
                    Category = "Not classified",
                    Status = concern.Status,
                    SubmittedAt = concern.SubmittedAt,
                    Attachments = concern.Attachments
                        .OrderBy(attachment => attachment.UploadedAt)
                        .Select(attachment => new RoutingAttachmentViewModel
                        {
                            Path = NormalizeAttachmentPath(attachment.FilePath),
                            Type = attachment.FileType
                        })
                        .ToList()
                })
                .ToList();

            var recommendationEntities = await _db.Recommendations
                .AsNoTracking()
                .Include(recommendation => recommendation.Attachments)
                .Where(recommendation => recommendation.Status == "Pending" &&
                    (recommendation.AssignedOffice == null || recommendation.AssignedOffice == ""))
                .ToListAsync();

            var recommendations = recommendationEntities
                .Select(recommendation => new UncategorizedSubmissionViewModel
                {
                    Id = recommendation.Id,
                    Type = "Recommendation",
                    Summary = recommendation.Title + " — " + recommendation.Description,
                    Title = recommendation.Title,
                    Description = recommendation.Description,
                    Justification = recommendation.Justification,
                    Location = recommendation.Location,
                    Beneficiaries = recommendation.Beneficiaries,
                    EstimatedPeopleAffected = recommendation.EstimatedPeopleAffected,
                    Category = string.IsNullOrWhiteSpace(recommendation.Category)
                        ? "Not classified"
                        : recommendation.Category,
                    Status = recommendation.Status,
                    SubmittedAt = recommendation.SubmittedAt,
                    Attachments = recommendation.Attachments
                        .OrderBy(attachment => attachment.UploadedAt)
                        .Select(attachment => new RoutingAttachmentViewModel
                        {
                            Path = NormalizeAttachmentPath(attachment.FilePath),
                            Type = attachment.FileType
                        })
                        .ToList()
                })
                .ToList();

            var submissions = concerns.Concat(recommendations);
            if (CurrentType != "All")
                submissions = submissions.Where(submission => submission.Type == CurrentType);

            UncategorizedSubmissions = CurrentSort == "Newest"
                ? submissions.OrderByDescending(submission => submission.SubmittedAt).ToList()
                : submissions.OrderBy(submission => submission.SubmittedAt).ToList();
        }

        private static string NormalizeAttachmentPath(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out _))
                return path;

            return "/" + path.TrimStart('~', '/', '\\').Replace('\\', '/');
        }
    }

    public class DepartmentAccuracyViewModel
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Correct { get; set; }
        public double AccuracyPercent { get; set; }
    }

    public class UncategorizedSubmissionViewModel
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Justification { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Beneficiaries { get; set; } = string.Empty;
        public int EstimatedPeopleAffected { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public List<RoutingAttachmentViewModel> Attachments { get; set; } = new();
    }

    public class RoutingAttachmentViewModel
    {
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
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
