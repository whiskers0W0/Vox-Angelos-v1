using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public string DepartmentName { get; set; } = string.Empty;
        public int TotalConcerns { get; set; }
        public int TotalUnresolved { get; set; }
        public int TotalChosen { get; set; }
        public int TotalInProgress { get; set; }
        public int TotalResolved { get; set; }

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            DepartmentName = user?.Department ?? "LGU";

            var concerns = _db.Concerns.AsQueryable();

            TotalConcerns = await concerns.CountAsync();
            TotalUnresolved = await concerns.CountAsync(c => c.Status == "Unresolved");
            TotalChosen = await concerns.CountAsync(c => c.Status == "Chosen");
            TotalInProgress = await concerns.CountAsync(c => c.Status == "In Progress");
            TotalResolved = await concerns.CountAsync(c => c.Status == "Resolved");
        }
    }
}