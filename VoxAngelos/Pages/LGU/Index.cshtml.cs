using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ConcernClassificationService _classifier;
        private readonly IHubContext<FeedHub> _feedHub;

        public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            ConcernClassificationService classifier, IHubContext<FeedHub> feedHub)
        {
            _db = db;
            _userManager = userManager;
            _classifier = classifier;
            _feedHub = feedHub;
        }

        // Notifies every LGU dashboard that could be displaying this concern — its
        // current department plus whatever it used to be, in case a reassign just moved
        // it out of one department's view and into another's.
        private Task NotifyDepartmentsAsync(params string?[] departments)
        {
            var tasks = departments
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .Select(d => _feedHub.Clients.Group(FeedHub.LguDepartmentGroup(d!)).SendAsync("ConcernFeedChanged"));
            return Task.WhenAll(tasks);
        }

        public string[] Departments => ConcernClassificationService.Departments;

        public List<ConcernViewModel> Concerns { get; set; } = new();
        public string CurrentFilter { get; set; } = "Unresolved";

        public async Task OnGetAsync(string? filter)
        {
            CurrentFilter = filter ?? "Unresolved";

            var user = await _userManager.GetUserAsync(User);
            var userDepartment = user?.Department;

            var query = _db.Concerns
                .Include(c => c.Attachments)
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .Where(c => c.Status != "Draft")
                .AsQueryable();

            // Show concerns whose classified category matches this LGU's department
            if (!string.IsNullOrEmpty(userDepartment))
            {
                query = query.Where(c => c.Category == userDepartment || c.Category == null);
            }

            if (CurrentFilter != "All")
            {
                query = query.Where(c => c.Status == CurrentFilter);
            }

            var reviewedConcernIds = (await _db.ClassificationCorrections
                .Select(cc => cc.ConcernId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            Concerns = await query
                .OrderByDescending(c => c.SubmittedAt)
                .Select(c => new ConcernViewModel
                {
                    Id = c.Id,
                    CitizenName = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName} {c.Citizen.UserProfile.LastName}"
                        : c.Citizen.Email,
                    Initials = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName[0]}{c.Citizen.UserProfile.LastName[0]}"
                        : "??",
                    Description = c.Description,
                    Category = c.Category ?? "Uncategorized",
                    RawCategory = c.Category,
                    Status = c.Status,
                    LocationName = c.LocationName ?? "No location provided",
                    Latitude = c.Latitude,
                    Longitude = c.Longitude,
                    LocationDensityScore = c.LocationDensityScore,
                    SubmittedAt = c.SubmittedAt,
                    FirstAttachmentPath = c.Attachments
                        .Where(a => a.FileType == "image")
                        .Select(a => a.FilePath)
                        .FirstOrDefault()
                })
                .ToListAsync();

            foreach (var concern in Concerns)
                concern.HasFeedback = reviewedConcernIds.Contains(concern.Id);
        }

        public async Task<IActionResult> OnPostConfirmCategoryAsync(int concernId)
        {
            var concern = await _db.Concerns.FindAsync(concernId);
            if (concern == null || string.IsNullOrEmpty(concern.Category)) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            try
            {
                await _classifier.RecordCorrectionAsync(concernId, concern.Category, wasCorrect: true, user!.Id);
            }
            catch (ConcernAlreadyReviewedException)
            {
                TempData["ConcernError"] = "This concern was already reviewed by another staff member.";
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        // Manual Override Feature: lets an LGU admin correct a concern that the Google
        // NLP classifier (or the local keyword fallback) routed to the wrong department,
        // re-routing it to the correct one. See docs/manual-override-feature.md for the
        // full write-up (why it exists, how the audit trail works, how it feeds the NLP
        // feedback loop in ConcernClassificationService.RecordCorrectionAsync).
        public async Task<IActionResult> OnPostReassignCategoryAsync(int concernId, string newCategory)
        {
            if (!ConcernClassificationService.Departments.Contains(newCategory))
                return BadRequest("Unknown department.");

            var previousCategory = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => c.Category)
                .FirstOrDefaultAsync();

            var user = await _userManager.GetUserAsync(User);
            try
            {
                await _classifier.RecordCorrectionAsync(concernId, newCategory, wasCorrect: false, user!.Id);
                await NotifyDepartmentsAsync(previousCategory, newCategory);
            }
            catch (ConcernAlreadyReviewedException)
            {
                TempData["ConcernError"] = "This concern was already reviewed by another staff member.";
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(
            int concernId, string status, string? notes)
        {
            // Guarded by current Status so two staff updating the same concern at once
            // can't overwrite each other — only the first update lands, atomically.
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId && c.Status != "Resolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.LguNotes, notes)
                    .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern could not be updated — it may already be resolved or no longer exist.";
            }
            else
            {
                var category = await _db.Concerns.Where(c => c.Id == concernId).Select(c => c.Category).FirstOrDefaultAsync();
                await NotifyDepartmentsAsync(category);
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostChooseConcernAsync(int concernId)
        {
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId && c.Status == "Unresolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, "Chosen")
                    .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern was already claimed by another staff member.";
            }
            else
            {
                var category = await _db.Concerns.Where(c => c.Id == concernId).Select(c => c.Category).FirstOrDefaultAsync();
                await NotifyDepartmentsAsync(category);
            }

            return RedirectToPage(new { filter = "Chosen" });
        }
    }

    public class ConcernViewModel
    {
        public int Id { get; set; }
        public string CitizenName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? RawCategory { get; set; }
        public bool HasFeedback { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int LocationDensityScore { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string? FirstAttachmentPath { get; set; }
    }
}
