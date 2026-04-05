using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class HeatmapModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
