#nullable disable
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        public string Email { get; set; }
        public bool Verified { get; set; }
        public decimal Confidence { get; set; }

        public IActionResult OnGet(string email, bool verified, decimal confidence, string returnUrl = null)
        {
            if (email == null)
            {
                return RedirectToPage("/Index");
            }

            Email = email;
            Verified = verified;
            Confidence = confidence;

            return Page();
        }
    }
}