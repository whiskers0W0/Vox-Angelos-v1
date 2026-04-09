using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class DashboardModel : PageModel
    {
        public string DepartmentName { get; set; } = "";
        public int TotalConcerns { get; set; }
        public int TotalUnresolved { get; set; }
        public int TotalInProgress { get; set; }
        public int TotalResolved { get; set; }

        public void OnGet()
        {
            // ── Department name from logged-in user ──
            var email = User.Identity?.Name ?? "";
            var prefix = email.Contains("@") ? email.Split('@')[0] : email;

            var departmentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "socialwelfare",  "Social Welfare" },
                { "publicsafety",   "Public Safety"  },
                { "health",         "Health"         },
                { "agriculture",    "Agriculture"    },
                { "engineering",    "Engineering"    },
                { "admin",          "Admin"          },
            };

            DepartmentName = departmentMap.TryGetValue(prefix, out var name) ? name : prefix;

            // ── Dummy data — replace with database queries later ──
            // e.g. TotalConcerns = _context.Concerns.Count(c => c.Department == DepartmentName);
            TotalConcerns = 0;
            TotalUnresolved = 0;
            TotalInProgress = 0;
            TotalResolved = 0;
        }
    }
}































