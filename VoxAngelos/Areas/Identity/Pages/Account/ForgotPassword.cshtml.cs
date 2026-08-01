#nullable disable
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using VoxAngelos.Data;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;

        public ForgotPasswordModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);

            // Only allow citizens to reset password
            if (user == null || !await _userManager.IsInRoleAsync(user, "User"))
            {
                // Don't reveal that the user does not exist
                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var resetPath = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code, email = user.Email });

            var publicBaseUrl = _configuration["App:PublicBaseUrl"];
            var hasValidPublicBaseUrl = Uri.TryCreate(
                publicBaseUrl,
                UriKind.Absolute,
                out var publicBaseUri) &&
                (publicBaseUri.Scheme == Uri.UriSchemeHttps ||
                 publicBaseUri.Scheme == Uri.UriSchemeHttp);

            var callbackUrl = hasValidPublicBaseUrl && !string.IsNullOrWhiteSpace(resetPath)
                ? new Uri(publicBaseUri!, resetPath).ToString()
                : Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code, email = user.Email },
                    protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(
                Input.Email,
                "Reset Your Vox Angelos Password",
                $"<h2>Password Reset Request</h2>" +
                $"<p>You requested to reset your Vox Angelos password.</p>" +
                $"<p>Click the link below to reset your password. This link expires in 1 hour.</p>" +
                $"<p><a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='background:#1a237e;color:#fff;padding:10px 20px;text-decoration:none;border-radius:6px;'>Reset Password</a></p>" +
                $"<p>If you did not request this, please ignore this email.</p>");

            return RedirectToPage("./ForgotPasswordConfirmation");
        }
    }
}
