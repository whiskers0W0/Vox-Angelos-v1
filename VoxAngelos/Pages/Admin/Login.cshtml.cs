using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<LoginModel> _logger;
        private readonly IHostEnvironment _env;

        public LoginModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            ILogger<LoginModel> logger,
            IHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
            _env = env;
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                ErrorMessage = "This account is locked. Please try again later.";
                return Page();
            }

            var passwordValid = await _userManager.CheckPasswordAsync(user, Password);
            if (!passwordValid)
            {
                await _userManager.AccessFailedAsync(user);
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            var otp = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
            _logger.LogWarning("Admin OTP for {Email}: {Otp}", user.Email, otp);

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Your Vox Angelos Admin Login Code",
                $"Admin login OTP for <strong>{user.Email}</strong>: <strong>{otp}</strong><br/>This code expires shortly. Do not share it with anyone.");

            TempData["Admin_2FA_UserId"] = user.Id;

            // Testing convenience: surface the OTP in the browser console since
            // SMTP delivery isn't reliable locally. Development-only.
            if (_env.IsDevelopment())
                TempData["Admin_2FA_DevOtp"] = otp;

            return RedirectToPage("/Admin/LoginOtp");
        }
    }
}
