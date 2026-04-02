using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public int UnreviewedProjects { get; set; }
        public int UnverifiedAccounts { get; set; }
        public int VerifiedAccounts { get; set; }

        public async Task OnGetAsync()
        {
            // Count pending citizen accounts
            var allUsers = await _userManager.GetUsersInRoleAsync("User");
            UnverifiedAccounts = allUsers.Count(u => u.ApprovalStatus == "Pending");
            VerifiedAccounts = allUsers.Count(u => u.ApprovalStatus == "Approved");

            // Projects — placeholder for now
            UnreviewedProjects = 0;
        }
    }
}