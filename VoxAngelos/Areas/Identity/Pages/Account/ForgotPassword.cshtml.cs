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

            var resolvedBaseUrl = publicBaseUrl ?? "https://voxangelos.onrender.com";
            var emailBody = BuildPasswordResetEmail(callbackUrl, resolvedBaseUrl);

            await _emailSender.SendEmailAsync(
                Input.Email,
                "Reset Your Vox Angelos Password",
                emailBody);

            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        private static string BuildPasswordResetEmail(string callbackUrl, string publicBaseUrl)
        {
            string baseUrl = publicBaseUrl?.TrimEnd('/') ?? "";

            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>Reset Your Password</title>
</head>
<body style=""margin:0; padding:0; background-color:#eaecf5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#eaecf5; padding:32px 16px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:480px; background-color:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 8px 24px rgba(22,70,160,0.12);"">

          <!-- Header -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2b45b0 0%,#1a2a6c 100%); background-color:#1646a0; padding:32px 40px; text-align:center;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 auto;"">
                <tr>
                  <td style=""vertical-align:middle;"">
                    <img src=""{baseUrl}/assets/VoxAngelosLogo.png"" alt=""Vox Angelos"" style=""height:40px; display:block; border:0px;"" />
                  </td>
                  <td style=""padding-left:12px; color:#ffffff; font-size:18px; font-weight:700; letter-spacing:-0.01em;"">
                    Vox Angelos
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:40px;"">
              <p style=""margin:0 0 8px; color:#1646a0; font-size:11px; font-weight:800; letter-spacing:0.1em; text-transform:uppercase;"">
                Account Recovery
              </p>
              <h1 style=""margin:0 0 16px; color:#172033; font-size:24px; font-weight:800; letter-spacing:-0.02em; line-height:1.25;"">
                Reset your password
              </h1>
              <p style=""margin:0 0 28px; color:#5c687b; font-size:14px; line-height:1.6;"">
                You requested to reset your Vox Angelos password. Click the button below to choose a new one. This link expires in <strong style=""color:#172033;"">1 hour</strong>.
              </p>

              <!-- Button -->
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""border-radius:8px; background-color:#1646a0;"">
                    <a href=""{callbackUrl}""
                       style=""display:inline-block; padding:14px 28px; color:#ffffff; font-size:13px; font-weight:700; letter-spacing:0.02em; text-transform:uppercase; text-decoration:none; border-radius:8px;"">
                      Reset Password
                    </a>
                  </td>
                </tr>
              </table>

              <p style=""margin:28px 0 0; color:#8994a6; font-size:12px; line-height:1.6;"">
                If the button doesn't work, copy and paste this link into your browser:<br />
                <a href=""{callbackUrl}"" style=""color:#1646a0; word-break:break-all;"">{callbackUrl}</a>
              </p>

              <p style=""margin:20px 0 0; color:#8994a6; font-size:12px; line-height:1.6;"">
                If you did not request this, you can safely ignore this email — your password will not be changed.
              </p>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:24px 40px; border-top:1px solid #eef1f6; background-color:#fafbfc;"">
              <p style=""margin:0; color:#a3adbd; font-size:11px; line-height:1.6;"">
                Protected by <strong style=""color:#5b6577;"">RA 10173</strong>. Your recovery data is handled in accordance with the Data Privacy Act of the Philippines.
              </p>
            </td>
          </tr>

        </table>

        <p style=""margin:20px 0 0; color:#a3adbd; font-size:11px;"">
          &copy; 2026 Vox Angelos
        </p>
      </td>
    </tr>
  </table>
</body>
</html>";
        }
    }
}