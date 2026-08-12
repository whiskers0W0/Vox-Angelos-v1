#nullable disable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly IConfiguration _configuration;

        public LoginWith2faModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginWith2faModel> logger,
            IEmailSender emailSender,
            ISmsSender smsSender,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _configuration = configuration;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string GeneratedOtp { get; set; }

        // Drives whether the page's copy says "email" or "email or phone number" —
        // true only for citizens with a phone number on file, since LGU/Admin
        // accounts never get the SMS leg. [TempData] so it survives the re-render
        // after a failed OnPostAsync attempt, same as GeneratedOtp below.
        [TempData]
        public bool OtpSentBySms { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(6, ErrorMessage = "The {0} must be exactly {1} digits.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            [Display(Name = "OTP Code")]
            public string TwoFactorCode { get; set; }

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
        {
            var userId = TempData.Peek("2FA_UserId")?.ToString();
            if (userId == null)
                return RedirectToPage("./Login");

            var otp = TempData.Peek("2FA_OTP")?.ToString();
            var user = await _userManager.FindByIdAsync(userId);

            if (user != null && otp != null)
            {
                _logger.LogWarning("OTP for {Email}: {Otp}", user.Email, otp);

                // Fetch public base url from configuration
                var publicBaseUrl = _configuration["App:PublicBaseUrl"] ?? "https://voxangelos.onrender.com";
                var emailBody = BuildLoginOtpEmail(otp, publicBaseUrl);

                await _emailSender.SendEmailAsync(
                    user.Email,
                    "Your Vox Angelos Login Code",
                    emailBody);

                // Citizens also get the code by SMS; LGU/Admin panel accounts stay email-only.
                OtpSentBySms = !string.IsNullOrWhiteSpace(user.PhoneNumber) && await _userManager.IsInRoleAsync(user, "User");
                if (OtpSentBySms)
                {
                    await _smsSender.SendSmsAsync(
                        user.PhoneNumber,
                        $"Your Vox Angelos login code is {otp}. It expires shortly. Do not share this code.");
                }

                GeneratedOtp = otp;
                TempData.Remove("2FA_OTP");
            }

            RememberMe = rememberMe;
            ReturnUrl = returnUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
        {
            if (!ModelState.IsValid)
                return Page();

            returnUrl ??= Url.Content("~/");

            var userId = TempData.Peek("2FA_UserId")?.ToString();
            var remember = TempData.Peek("2FA_RememberMe") is bool b && b;

            if (userId == null)
            {
                ModelState.AddModelError(string.Empty, "Session expired. Please log in again.");
                return Page();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User not found.");
                return Page();
            }

            // Track OTP attempts
            var attemptsKey = $"2FA_Attempts_{userId}";
            int attempts = TempData.Peek(attemptsKey) is int a ? a : 0;

            var otp = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

            // Use Identity's built-in verifier — single use, time-limited
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultEmailProvider, otp);

            if (!isValid)
            {
                attempts++;
                TempData[attemptsKey] = attempts;

                if (attempts >= 3)
                {
                    // Force hard lockout for 5 minutes
                    await _userManager.SetLockoutEnabledAsync(user, true);
                    await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
                    TempData["LockedOutEmail"] = user.Email;

                    TempData.Remove("2FA_UserId");
                    TempData.Remove("2FA_RememberMe");
                    TempData.Remove(attemptsKey);
                    return RedirectToPage("./Lockout");
                }

                int remaining = 3 - attempts;
                ModelState.AddModelError(string.Empty,
                    $"Invalid OTP code. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining.");
                return Page();
            }

            // OTP correct — clear session
            TempData.Remove("2FA_UserId");
            TempData.Remove("2FA_RememberMe");
            TempData.Remove(attemptsKey);

            await _signInManager.SignInAsync(user, remember);
            _logger.LogInformation("User logged in with OTP.");

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin")) return RedirectToPage("/Admin/Index");
            if (roles.Contains("LGU")) return RedirectToPage("/LGU/Dashboard");
            if (roles.Contains("User"))
            {
                // Citizens may return only to a local page inside their own workspace.
                // This supports the landing-page submission handoff without allowing
                // an external redirect or access to another role's area.
                if (Url.IsLocalUrl(returnUrl) &&
                    returnUrl.StartsWith("/User/", StringComparison.OrdinalIgnoreCase))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToPage("/User/Index");
            }

            return RedirectToPage("./Login");
        }

        private static string BuildLoginOtpEmail(string otp, string publicBaseUrl)
        {
            string baseUrl = publicBaseUrl?.TrimEnd('/') ?? "";

            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>Your Login Code</title>
</head>
<body style=""margin:0; padding:0; background-color:#eaecf5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#eaecf5; padding:32px 16px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:480px; background-color:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 8px 24px rgba(22,70,160,0.12);"">

          <!-- Header with Logo Replacement --> 
          <tr>   
            <td style=""background:linear-gradient(135deg,#2b45b0 0%,#1a2a6c 100%); background-color:#1646a0; padding:32px 40px; text-align:center;"">     
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 auto;"">       
                <tr>         
                  <td style=""vertical-align:middle;"">           
                    <img src=""{baseUrl}/assets/VoxAngelosLogo.png"" alt=""Vox Angelos Logo"" style=""height:40px; display:block; border:0px;"" />         
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
                Authentication Required
              </p>
              <h1 style=""margin:0 0 16px; color:#172033; font-size:24px; font-weight:800; letter-spacing:-0.02em; line-height:1.25;"">
                Your login code
              </h1>
              <p style=""margin:0 0 24px; color:#5c687b; font-size:14px; line-height:1.6;"">
                Please enter the following 6-digit code to complete your login. This code expires shortly.
              </p>

              <!-- OTP Box Display -->
              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#f4f6fb; border:1px solid #e2e8f0; border-radius:12px; margin-bottom:24px;"">
                <tr>
                  <td align=""center"" style=""padding:20px; font-size:32px; font-weight:800; letter-spacing:0.25em; color:#1646a0;"">
                    {otp}
                  </td>
                </tr>
              </table>

              <p style=""margin:0; color:#8994a6; font-size:12px; line-height:1.6;"">
                If you did not attempt to log in, please secure your account immediately. Do not share this code with anyone.
              </p>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:24px 40px; border-top:1px solid #eef1f6; background-color:#fafbfc;"">
              <p style=""margin:0; color:#a3adbd; font-size:11px; line-height:1.6;"">
                Protected by <strong style=""color:#5b6577;"">RA 10173</strong>. Your authentication data is handled in accordance with the Data Privacy Act of the Philippines.
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
