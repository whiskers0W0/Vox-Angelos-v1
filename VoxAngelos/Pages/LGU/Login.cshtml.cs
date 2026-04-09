using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config,
            ILogger<LoginModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _logger = logger;
        }

        [BindProperty]
        public string EmployeeId { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        public string SecurityKey { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            // 1. Check security key first
            var validKey = _config["SecurityKeys:LGU"];
            if (SecurityKey != validKey)
            {
                ErrorMessage = "Invalid security key.";
                return Page();
            }

            // 2. Find user by EmployeeId
            var user = _userManager.Users
                .FirstOrDefault(u => u.EmployeeId == EmployeeId);

            if (user == null)
            {
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            // 3. Confirm they are actually an LGU user
            if (!await _userManager.IsInRoleAsync(user, "LGU"))
            {
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            // 4. Check lockout
            if (await _userManager.IsLockedOutAsync(user))
            {
                ErrorMessage = "This account is locked. Please try again later.";
                return Page();
            }

            // 5. Verify password
            var passwordValid = await _userManager.CheckPasswordAsync(user, Password);
            if (!passwordValid)
            {
                await _userManager.AccessFailedAsync(user);
                ErrorMessage = "Invalid credentials.";
                return Page();
            }

            // 6. Sign in
            await _userManager.ResetAccessFailedCountAsync(user);
            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("LGU {EmployeeId} ({Department}) logged in.",
                EmployeeId, user.Department);

            return RedirectToPage("/LGU/Dashboard");
        }
    }
}