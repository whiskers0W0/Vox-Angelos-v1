using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class LeaderboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
