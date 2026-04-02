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

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly IEmailSender _emailSender;

        public LoginWith2faModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginWith2faModel> logger,
            IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string GeneratedOtp { get; set; }

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

                await _emailSender.SendEmailAsync(
                    user.Email,
                    "Your Vox Angelos Login Code",
                    $"Your 6-digit login code is: <strong>{otp}</strong><br/>This code expires shortly. Do not share it with anyone.");

                // Remove OTP from TempData after sending so it can't be read client-side
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
            if (roles.Contains("LGU")) return RedirectToPage("/LGU/Index");
            if (roles.Contains("User")) return RedirectToPage("/User/Index");

            return LocalRedirect(returnUrl);
        }
    }
}