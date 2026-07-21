using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [AllowAnonymous]
    public class LoginOtpModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginOtpModel> _logger;

        public LoginOtpModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<LoginOtpModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        [BindProperty]
        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be exactly 6 digits.")]
        public string OtpCode { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public IActionResult OnGet()
        {
            if (TempData.Peek("LGU_2FA_UserId") == null)
                return RedirectToPage("/LGU/Login");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var userId = TempData.Peek("LGU_2FA_UserId")?.ToString();
            if (userId == null)
            {
                ErrorMessage = "Session expired. Please log in again.";
                return RedirectToPage("/LGU/Login");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                ErrorMessage = "User not found.";
                return RedirectToPage("/LGU/Login");
            }

            var attemptsKey = $"LGU_2FA_Attempts_{userId}";
            int attempts = TempData.Peek(attemptsKey) is int a ? a : 0;

            var code = OtpCode.Replace(" ", "").Replace("-", "");
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultEmailProvider, code);

            if (!isValid)
            {
                attempts++;
                TempData[attemptsKey] = attempts;

                if (attempts >= 3)
                {
                    await _userManager.SetLockoutEnabledAsync(user, true);
                    await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
                    TempData.Remove("LGU_2FA_UserId");
                    TempData.Remove(attemptsKey);
                    return RedirectToPage("/LGU/Login");
                }

                int remaining = 3 - attempts;
                ErrorMessage = $"Invalid OTP code. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining.";
                return Page();
            }

            TempData.Remove("LGU_2FA_UserId");
            TempData.Remove(attemptsKey);

            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("LGU {Email} ({Department}) logged in with OTP.", user.Email, user.Department);

            return RedirectToPage("/LGU/Dashboard");
        }
    }
}
