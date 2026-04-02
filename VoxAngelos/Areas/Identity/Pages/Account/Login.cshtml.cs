// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using VoxAngelos.Data;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _logger = logger;
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid)
                return Page();

            // Find user by email
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Check lockout
            if (await _userManager.IsLockedOutAsync(user))
            {
                TempData["LockedOutEmail"] = Input.Email;
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            // Verify password
            var passwordValid = await _userManager.CheckPasswordAsync(user, Input.Password);
            if (!passwordValid)
            {
                await _userManager.AccessFailedAsync(user);
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Reset failed count on success
            await _userManager.ResetAccessFailedCountAsync(user);

            // Get roles once — used throughout
            var userRoles = await _userManager.GetRolesAsync(user);

            // Block unapproved citizens
            if (userRoles.Contains("User") && user.ApprovalStatus != "Approved")
            {
                ModelState.AddModelError(string.Empty,
                    "Your account is pending admin approval. Please check back later.");
                return Page();
            }

            // Block Admin and LGU from using citizen login
            if (userRoles.Contains("Admin") || userRoles.Contains("LGU"))
            {
                ModelState.AddModelError(string.Empty,
                    "Please use the appropriate portal to log in.");
                return Page();
            }

            // If 2FA is enabled, redirect to OTP page
            if (user.TwoFactorEnabled)
            {
                var otp = await _userManager.GenerateTwoFactorTokenAsync(
                    user, TokenOptions.DefaultEmailProvider);
                _logger.LogWarning("OTP for {Email}: {Otp}", user.Email, otp);

                TempData["2FA_UserId"] = user.Id;
                TempData["2FA_OTP"] = otp;
                TempData["2FA_RememberMe"] = Input.RememberMe;

                return RedirectToPage("./LoginWith2fa",
                    new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }

            // No 2FA — sign in directly
            await _signInManager.SignInAsync(user, Input.RememberMe);
            _logger.LogInformation("User logged in.");

            if (userRoles.Contains("User")) return RedirectToPage("/User/Index");

            return LocalRedirect(returnUrl);
        }
    }
}