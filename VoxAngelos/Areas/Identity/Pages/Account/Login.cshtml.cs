// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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
        private readonly IConfiguration _configuration;
        private static readonly HttpClient RecaptchaClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            ILogger<LoginModel> logger,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _logger = logger;
            _userManager = userManager;
            _configuration = configuration;
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

            var recaptchaToken = Request.Form["g-recaptcha-response"].ToString();
            if (string.IsNullOrWhiteSpace(recaptchaToken))
            {
                ModelState.AddModelError(string.Empty,
                    "Please confirm that you are not a robot before signing in.");
                return Page();
            }

            if (!await VerifyRecaptchaAsync(recaptchaToken))
                return Page();

            // Find user by email
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Accounts created before lockout was enabled for new users can be stuck with
            // LockoutEnabled = false, which silently disables lockout even though failed
            // attempts keep being counted. Backfill it here so every account is covered.
            if (!user.LockoutEnabled)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
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

                // This attempt may have just crossed MaxFailedAccessAttempts. Re-check
                // immediately so the account locks out on the attempt that trips it,
                // instead of waiting for the user to try once more.
                if (await _userManager.IsLockedOutAsync(user))
                {
                    TempData["LockedOutEmail"] = Input.Email;
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }

                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Reset failed count on success
            await _userManager.ResetAccessFailedCountAsync(user);

            // Get roles once — used throughout
            var userRoles = await _userManager.GetRolesAsync(user);

            var isAdmin = userRoles.Contains("Admin");
            var isLgu = userRoles.Contains("LGU");
            var isCitizen = userRoles.Contains("User");

            // Citizen accounts require approval. Staff roles bypass this citizen-only
            // requirement, so a valid Admin or LGU account can use the shared login.
            if (!isAdmin && !isLgu && isCitizen && user.ApprovalStatus != "Approved")
            {
                ModelState.AddModelError(string.Empty,
                    "Your account is pending admin approval. Please check back later.");
                return Page();
            }

            if (!isAdmin && !isLgu && !isCitizen)
            {
                ModelState.AddModelError(string.Empty,
                    "This account does not have permission to access Vox Angelos.");
                return Page();
            }

            // Every recognized role must complete the existing email OTP step.
            // This keeps the former Citizen, LGU, and Admin login security behavior
            // while allowing all roles to start from the same login page.
            var otp = await _userManager.GenerateTwoFactorTokenAsync(
                user, TokenOptions.DefaultEmailProvider);
            _logger.LogWarning("OTP for {Email}: {Otp}", user.Email, otp);

            TempData["2FA_UserId"] = user.Id;
            TempData["2FA_OTP"] = otp;
            TempData["2FA_RememberMe"] = Input.RememberMe;

            return RedirectToPage("./LoginWith2fa",
                new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
        }

        private async Task<bool> VerifyRecaptchaAsync(string token)
        {
            var secretKey = _configuration["Recaptcha:SecretKey"];
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                _logger.LogError("The reCAPTCHA secret key is not configured.");
                ModelState.AddModelError(string.Empty,
                    "Security verification is temporarily unavailable. Please try again later.");
                return false;
            }

            try
            {
                using var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secretKey,
                    ["response"] = token
                });

                using var response = await RecaptchaClient.PostAsync(
                    "https://www.google.com/recaptcha/api/siteverify",
                    requestContent,
                    HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                var verification = await response.Content.ReadFromJsonAsync<RecaptchaVerificationResponse>(
                    cancellationToken: HttpContext.RequestAborted);

                if (verification?.Success == true)
                    return true;

                var errorCodes = verification?.ErrorCodes ?? Array.Empty<string>();
                _logger.LogWarning(
                    "reCAPTCHA rejected a login attempt. Error codes: {ErrorCodes}",
                    string.Join(", ", errorCodes));

                var message = errorCodes.Contains("timeout-or-duplicate")
                    ? "Security verification expired. Please complete the checkbox again."
                    : "We could not verify that you are not a robot. Please try again.";

                ModelState.AddModelError(string.Empty, message);
                return false;
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "The reCAPTCHA verification request failed.");
            }
            catch (TaskCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogError(exception, "The reCAPTCHA verification request timed out.");
            }

            ModelState.AddModelError(string.Empty,
                "Security verification is temporarily unavailable. Please try again later.");
            return false;
        }

        private sealed class RecaptchaVerificationResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("error-codes")]
            public string[] ErrorCodes { get; set; } = Array.Empty<string>();
        }
    }
}
