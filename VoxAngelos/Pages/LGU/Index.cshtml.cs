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
        private static readonly IReadOnlyDictionary<string, string> DepartmentDisplayNames =
            new Dictionary<string, string>
            {
                ["SWDO"] = "Social Welfare and Development Office",
                ["CEO"] = "City Engineer's Office",
                ["CENRO"] = "City Environment and Natural Resources Office",
                ["ACDO"] = "City Development / Urban Planning Office",
                ["PTRO"] = "Public Safety, Traffic and Transport Regulation Office",
                ["OSCA"] = "Office of Senior Citizens Affairs",
                ["PWDAO"] = "Persons With Disability Affairs Office"
            };

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

        private static string GetDepartmentDisplayName(string department) =>
            DepartmentDisplayNames.GetValueOrDefault(department, department);

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

        private enum ReassignOutcome { Success, NotFound, Forbidden, NotEligible, AlreadyReviewed }

        // Shared by the single-concern reassign handler and the bulk one below, so both
        // go through the exact same audit-trail write (ClassificationCorrection + learned
        // weights via RecordCorrectionAsync), citizen notification, and timeline event —
        // a bulk reassign is just this run in a loop, not a separate code path.
        private async Task<ReassignOutcome> TryReassignConcernAsync(int concernId, string newCategory, ApplicationUser user)
        {
            var concern = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => new { c.Category, c.CitizenId, c.Status })
                .FirstOrDefaultAsync();
            if (concern == null) return ReassignOutcome.NotFound;

            // Uncategorized concerns (Category == null) are open to any LGU office to
            // claim and route — only block hijacking a concern another office already owns.
            if (concern.Category != null && user.Department != concern.Category) return ReassignOutcome.Forbidden;

            if (concern.Status != "Unresolved") return ReassignOutcome.NotEligible;

            try
            {
                await _classifier.RecordCorrectionAsync(concernId, newCategory, wasCorrect: false, user.Id);

                var forwardedAt = DateTime.UtcNow;
                var actorName = user.Department ?? user.Email ?? "LGU Office";
                var updateMessage = $"Your concern was forwarded to the {GetDepartmentDisplayName(newCategory)} for review.";

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Concern Forwarded",
                    Status = "Unresolved",
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = forwardedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was forwarded",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Notifications",
                    CreatedAt = forwardedAt
                });

                await _db.SaveChangesAsync();
                return ReassignOutcome.Success;
            }
            catch (ConcernAlreadyReviewedException)
            {
                return ReassignOutcome.AlreadyReviewed;
            }
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

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            var previousCategory = await _db.Concerns
                .Where(c => c.Id == concernId)
                .Select(c => c.Category)
                .FirstOrDefaultAsync();

            var outcome = await TryReassignConcernAsync(concernId, newCategory, user);

            switch (outcome)
            {
                case ReassignOutcome.NotFound:
                    return NotFound();
                case ReassignOutcome.Forbidden:
                    return Forbid();
                case ReassignOutcome.NotEligible:
                    TempData["ConcernError"] = "Only unresolved concerns can be reassigned. Once an office accepts a concern, it must finish or escalate it through the LGU workflow.";
                    break;
                case ReassignOutcome.AlreadyReviewed:
                    TempData["ConcernError"] = "This concern was already reviewed by another staff member.";
                    break;
                case ReassignOutcome.Success:
                    await NotifyDepartmentsAsync(previousCategory, newCategory);
                    break;
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        // Bulk version of the reassign feature above — lets an LGU staffer route many
        // uncategorized/misrouted concerns to the correct department in one submit
        // instead of clicking through the single-item modal for each one.
        public async Task<IActionResult> OnPostBulkReassignCategoryAsync(int[] concernIds, string newCategory, string? filter)
        {
            CurrentFilter = filter ?? "Unresolved";

            if (!ConcernClassificationService.Departments.Contains(newCategory))
                return BadRequest("Unknown department.");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Forbid();

            if (concernIds == null || concernIds.Length == 0)
            {
                TempData["ConcernError"] = "No concerns were selected.";
                return RedirectToPage(new { filter = CurrentFilter });
            }

            var touchedCategories = new HashSet<string?> { newCategory };
            int succeeded = 0, skipped = 0;

            foreach (var concernId in concernIds.Distinct())
            {
                var previousCategory = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => c.Category)
                    .FirstOrDefaultAsync();

                var outcome = await TryReassignConcernAsync(concernId, newCategory, user);
                if (outcome == ReassignOutcome.Success)
                {
                    succeeded++;
                    touchedCategories.Add(previousCategory);
                }
                else
                {
                    skipped++;
                }
            }

            await NotifyDepartmentsAsync(touchedCategories.ToArray());

            TempData["ConcernSuccess"] = skipped == 0
                ? $"Reassigned {succeeded} concern(s) to {GetDepartmentDisplayName(newCategory)}."
                : $"Reassigned {succeeded} concern(s) to {GetDepartmentDisplayName(newCategory)}. {skipped} were skipped (already resolved, already reviewed, or owned by another office).";

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(
            int concernId, string status, string? notes)
        {
            var updatedAt = DateTime.UtcNow;

            // Guarded by current Status so two staff updating the same concern at once
            // can't overwrite each other — only the first update lands, atomically.
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId && c.Status != "Resolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.LguNotes, notes)
                    .SetProperty(c => c.UpdatedAt, updatedAt));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern could not be updated — it may already be resolved or no longer exist.";
            }
            else
            {
                var concern = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => new { c.Category, c.CitizenId })
                    .SingleAsync();
                var lguUser = await _userManager.GetUserAsync(User);
                var actorName = lguUser?.Department ?? lguUser?.Email ?? "LGU Office";
                var updateMessage = string.IsNullOrWhiteSpace(notes)
                    ? $"The LGU updated the concern status to {status}."
                    : notes;

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Status Updated",
                    Status = status,
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = updatedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was updated",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Notifications",
                    CreatedAt = updatedAt
                });
                await _db.SaveChangesAsync();

                await NotifyDepartmentsAsync(concern.Category);
            }

            return RedirectToPage(new { filter = CurrentFilter });
        }

        public async Task<IActionResult> OnPostChooseConcernAsync(int concernId)
        {
            var updatedAt = DateTime.UtcNow;
            var updated = await _db.Concerns
                .Where(c => c.Id == concernId && c.Status == "Unresolved")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, "Chosen")
                    .SetProperty(c => c.UpdatedAt, updatedAt));

            if (updated == 0)
            {
                TempData["ConcernError"] = "This concern was already claimed by another staff member.";
            }
            else
            {
                var concern = await _db.Concerns
                    .Where(c => c.Id == concernId)
                    .Select(c => new { c.Category, c.CitizenId })
                    .SingleAsync();
                var lguUser = await _userManager.GetUserAsync(User);
                var actorName = lguUser?.Department ?? lguUser?.Email ?? "LGU Office";
                const string updateMessage = "An LGU office has accepted this concern for action.";

                _db.ConcernTimelineEvents.Add(new ConcernTimelineEvent
                {
                    ConcernId = concernId,
                    EventType = "Concern Chosen",
                    Status = "Chosen",
                    Message = updateMessage,
                    ActorRole = "LGU",
                    ActorName = actorName,
                    CreatedAt = updatedAt
                });

                _db.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = concern.CitizenId,
                    Title = "Your concern was accepted",
                    Message = updateMessage,
                    NotificationType = "ConcernUpdate",
                    SenderRole = "LGU",
                    SenderName = actorName,
                    LinkUrl = "/User/Notifications",
                    CreatedAt = updatedAt
                });
                await _db.SaveChangesAsync();

                await NotifyDepartmentsAsync(concern.Category);
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
