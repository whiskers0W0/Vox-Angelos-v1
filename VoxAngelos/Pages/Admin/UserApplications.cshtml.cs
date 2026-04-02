using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class UserApplicationsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserApplicationsModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public List<CitizenApplicationViewModel> Applications { get; set; } = new();

        public string FilterStatus { get; set; } = "All";

        public async Task OnGetAsync(string filterStatus = "All")
        {
            FilterStatus = filterStatus;

            var citizenUsers = await _userManager.GetUsersInRoleAsync("User");

            var query = citizenUsers.AsQueryable();

            if (filterStatus != "All")
                query = query.Where(u => u.ApprovalStatus == filterStatus);

            var userIds = query.Select(u => u.Id).ToList();

            var profiles = await _context.UserProfiles
                .Where(p => userIds.Contains(p.UserId))
                .ToListAsync();

            var faceVerifications = await _context.UserFaceVerifications
                .Where(f => userIds.Contains(f.UserId))
                .ToListAsync();

            foreach (var user in query.OrderBy(u => u.CreatedAt))
            {
                var profile = profiles.FirstOrDefault(p => p.UserId == user.Id);
                var face = faceVerifications.FirstOrDefault(f => f.UserId == user.Id);

                Applications.Add(new CitizenApplicationViewModel
                {
                    UserId = user.Id,
                    FullName = profile != null
                        ? $"{profile.FirstName} {profile.MiddleName} {profile.LastName}".Trim()
                        : user.Email,
                    Email = user.Email,
                    ContactNumber = user.PhoneNumber ?? "N/A",
                    DateApplied = user.CreatedAt,
                    ApprovalStatus = user.ApprovalStatus,
                    FaceMatchConfidence = face?.MatchConfidence ?? 0
                });
            }
        }

        public async Task<IActionResult> OnPostApproveAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.ApprovalStatus = "Approved";
                await _userManager.UpdateAsync(user);
            }
            return RedirectToPage(new { filterStatus = FilterStatus });
        }

        public async Task<IActionResult> OnPostRejectAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.ApprovalStatus = "Rejected";
                await _userManager.UpdateAsync(user);
            }
            return RedirectToPage(new { filterStatus = FilterStatus });
        }
    }

    public class CitizenApplicationViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public DateTime DateApplied { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public decimal FaceMatchConfidence { get; set; }
    }
}