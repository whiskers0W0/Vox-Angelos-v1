using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class AccountSecurityModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IMemoryCache _cache;
        private readonly bool _showOtpPreview;

        private const int MaximumOtpAttempts = 3;
        private static readonly TimeSpan PendingChangeLifetime = TimeSpan.FromMinutes(10);

        public AccountSecurityModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            IMemoryCache cache,
            IHostEnvironment environment,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _cache = cache;
            _showOtpPreview = environment.IsDevelopment()
                && configuration.GetValue<bool>("Email:ShowOtpInBrowser");
        }

        [BindProperty]
        public PasswordChangeInput PasswordInput { get; set; } = new();

        [BindProperty]
        public EmailChangeInput EmailInput { get; set; } = new();

        [BindProperty]
        public OtpVerificationInput OtpInput { get; set; } = new();

        public string CurrentEmail { get; private set; } = string.Empty;
        public PendingSecurityChangeType? PendingChangeType { get; private set; }

        [TempData]
        public string? OtpPreview { get; set; }

        [TempData]
        public string? PendingNewEmail { get; set; }

        [TempData]
        public string? EmailConfirmationPreview { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            SetPageValues(user);
            return Page();
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            RemoveModelStateFor(nameof(EmailInput));
            RemoveModelStateFor(nameof(OtpInput));
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            if (string.IsNullOrWhiteSpace(PasswordInput.CurrentPassword)
                || string.IsNullOrWhiteSpace(PasswordInput.NewPassword)
                || string.IsNullOrWhiteSpace(PasswordInput.ConfirmPassword)
                || PasswordInput.NewPassword.Length < 8
                || !string.Equals(PasswordInput.NewPassword, PasswordInput.ConfirmPassword, StringComparison.Ordinal))
            {
                TempData["SecurityError"] = "Please complete the password fields correctly before requesting a verification code.";
                return RedirectToPage();
            }

            if (!await _userManager.CheckPasswordAsync(user, PasswordInput.CurrentPassword))
            {
                ModelState.AddModelError($"{nameof(PasswordInput)}.{nameof(PasswordInput.CurrentPassword)}", "Your current password is incorrect.");
                TempData["SecurityError"] = "The current password you entered is incorrect.";
                return RedirectToPage();
            }

            if (await _userManager.CheckPasswordAsync(user, PasswordInput.NewPassword))
            {
                ModelState.AddModelError($"{nameof(PasswordInput)}.{nameof(PasswordInput.NewPassword)}", "Choose a new password that is different from your current password.");
                TempData["SecurityError"] = "Choose a new password that is different from your current password.";
                return RedirectToPage();
            }

            await StartOtpVerificationAsync(user, PendingSecurityChangeType.Password, PasswordInput.NewPassword);
            TempData["SecuritySuccess"] = "A 6-digit verification code was sent to your current email address. Enter it below to change your password.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRequestEmailChangeAsync()
        {
            RemoveModelStateFor(nameof(PasswordInput));
            RemoveModelStateFor(nameof(OtpInput));
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var newEmail = EmailInput.NewEmail?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(EmailInput.CurrentPassword)
                || string.IsNullOrWhiteSpace(newEmail)
                || !new EmailAddressAttribute().IsValid(newEmail))
            {
                TempData["SecurityError"] = "Enter a valid new email address and your current password before requesting a verification code.";
                return RedirectToPage();
            }

            if (!await _userManager.CheckPasswordAsync(user, EmailInput.CurrentPassword))
            {
                ModelState.AddModelError($"{nameof(EmailInput)}.{nameof(EmailInput.CurrentPassword)}", "Your current password is incorrect.");
                TempData["SecurityError"] = "The current password you entered is incorrect.";
                return RedirectToPage();
            }

            if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError($"{nameof(EmailInput)}.{nameof(EmailInput.NewEmail)}", "Enter a different email address.");
                TempData["SecurityError"] = "Enter an email address that is different from your current one.";
                return RedirectToPage();
            }

            var existingUser = await _userManager.FindByEmailAsync(newEmail);
            if (existingUser != null)
            {
                ModelState.AddModelError($"{nameof(EmailInput)}.{nameof(EmailInput.NewEmail)}", "That email address is already in use.");
                TempData["SecurityError"] = "That email address is already in use by another account.";
                return RedirectToPage();
            }

            await StartOtpVerificationAsync(user, PendingSecurityChangeType.Email, newEmail);
            TempData["SecuritySuccess"] = "A 6-digit verification code was sent to your current email address. Enter it below to continue changing your email.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostVerifyOtpAsync()
        {
            RemoveModelStateFor(nameof(PasswordInput));
            RemoveModelStateFor(nameof(EmailInput));
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var pendingChange = GetPendingChange(user.Id);
            if (pendingChange == null)
            {
                TempData["SecurityError"] = "That verification request expired. Start the change again.";
                return RedirectToPage();
            }

            if (string.IsNullOrWhiteSpace(OtpInput.Code)
                || OtpInput.Code.Replace(" ", string.Empty).Replace("-", string.Empty).Length != 6)
            {
                TempData["SecurityError"] = "Enter the 6-digit verification code.";
                return RedirectToPage();
            }

            var otp = OtpInput.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, otp);
            if (!isValid)
            {
                pendingChange.Attempts++;
                if (pendingChange.Attempts >= MaximumOtpAttempts)
                {
                    RemovePendingChange(user.Id);
                    TempData["SecurityError"] = "Too many incorrect codes. For your protection, the requested change was cancelled. Start again to receive a new code.";
                    return RedirectToPage();
                }

                SavePendingChange(user.Id, pendingChange);
                var remaining = MaximumOtpAttempts - pendingChange.Attempts;
                ModelState.AddModelError($"{nameof(OtpInput)}.{nameof(OtpInput.Code)}", $"Invalid verification code. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining.");
                TempData["SecurityError"] = $"Invalid verification code. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining.";
                return RedirectToPage();
            }

            RemovePendingChange(user.Id);

            if (pendingChange.Type == PendingSecurityChangeType.Password)
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, resetToken, pendingChange.ProposedValue);
                if (!result.Succeeded)
                {
                    TempData["SecurityError"] = "Your password could not be changed. Start again and use your current password.";
                    return RedirectToPage();
                }

                await _userManager.UpdateSecurityStampAsync(user);
                await _signInManager.RefreshSignInAsync(user);
                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Your Vox Angelos password was changed",
                    "<p>Your Vox Angelos password was changed successfully after OTP verification.</p><p>If you did not make this change, reset your password immediately.</p>");

                TempData["SecuritySuccess"] = "Your password was changed successfully.";
                return RedirectToPage();
            }

            var newEmail = pendingChange.ProposedValue;
            var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var confirmationUrl = Url.Page(
                "/User/AccountSecurity",
                pageHandler: "ConfirmEmail",
                values: new { userId = user.Id, newEmail, code = encodedToken },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(
                newEmail,
                "Confirm your new Vox Angelos email",
                $"<p>You verified this request with a code from your current email address.</p><p>Confirm that you own <strong>{HtmlEncoder.Default.Encode(newEmail)}</strong> by selecting the link below.</p>" +
                $"<p><a href='{HtmlEncoder.Default.Encode(confirmationUrl!)}'>Confirm new email address</a></p>" +
                "<p>You must be signed in to your Vox Angelos account to complete this change.</p>");

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await _emailSender.SendEmailAsync(
                    user.Email,
                    "Vox Angelos email change awaiting confirmation",
                    $"<p>A verified request to change your email to <strong>{HtmlEncoder.Default.Encode(newEmail)}</strong> is awaiting confirmation.</p>" +
                    "<p>No change will occur unless the new email address is confirmed. If this was not you, reset your password immediately.</p>");
            }

            PendingNewEmail = newEmail;
            if (_showOtpPreview)
                EmailConfirmationPreview = confirmationUrl;

            TempData["SecuritySuccess"] = "Your code was verified. Your email address has not changed yet: confirm ownership of the new email address to finish the change.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnGetConfirmEmailAsync(string userId, string newEmail, string code)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null || currentUser.Id != userId) return Forbid();

            try
            {
                var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                var result = await _userManager.ChangeEmailAsync(currentUser, newEmail, token);

                if (!result.Succeeded)
                {
                    TempData["SecurityError"] = "The email confirmation link is invalid or has expired. Request another email change.";
                    return RedirectToPage();
                }

                var usernameResult = await _userManager.SetUserNameAsync(currentUser, newEmail);
                if (!usernameResult.Succeeded)
                {
                    TempData["SecurityError"] = "Your email was changed, but your sign-in name could not be updated. Please contact an administrator.";
                    return RedirectToPage();
                }

                await _signInManager.RefreshSignInAsync(currentUser);
                TempData["SecuritySuccess"] = "Your email address was changed successfully.";
            }
            catch (FormatException)
            {
                TempData["SecurityError"] = "The email confirmation link is invalid. Request another email change.";
            }

            return RedirectToPage();
        }

        private void RemoveModelStateFor(string propertyName)
        {
            foreach (var key in ModelState.Keys
                .Where(key => key.StartsWith($"{propertyName}.", StringComparison.Ordinal))
                .ToList())
            {
                ModelState.Remove(key);
            }
        }

        private async Task StartOtpVerificationAsync(
            ApplicationUser user,
            PendingSecurityChangeType type,
            string proposedValue)
        {
            var otp = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
            SavePendingChange(user.Id, new PendingSecurityChange(type, proposedValue));

            if (_showOtpPreview)
                OtpPreview = otp;

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Your Vox Angelos account security code",
                $"<p>Your 6-digit verification code is: <strong>{otp}</strong></p><p>This code expires shortly. Do not share it with anyone.</p>");
        }

        private void SetPageValues(ApplicationUser user)
        {
            CurrentEmail = user.Email ?? string.Empty;
            PendingChangeType = GetPendingChange(user.Id)?.Type;
        }

        private PendingSecurityChange? GetPendingChange(string userId) =>
            _cache.Get<PendingSecurityChange>(GetPendingChangeKey(userId));

        private void SavePendingChange(string userId, PendingSecurityChange pendingChange) =>
            _cache.Set(GetPendingChangeKey(userId), pendingChange, PendingChangeLifetime);

        private void RemovePendingChange(string userId) =>
            _cache.Remove(GetPendingChangeKey(userId));

        private static string GetPendingChangeKey(string userId) => $"account-security-change:{userId}";

        public enum PendingSecurityChangeType
        {
            Password,
            Email
        }

        private sealed class PendingSecurityChange
        {
            public PendingSecurityChange(PendingSecurityChangeType type, string proposedValue)
            {
                Type = type;
                ProposedValue = proposedValue;
            }

            public PendingSecurityChangeType Type { get; }
            public string ProposedValue { get; }
            public int Attempts { get; set; }

        }

        public class PasswordChangeInput
        {
            [Required]
            [DataType(DataType.Password)]
            public string CurrentPassword { get; set; } = string.Empty;

            [Required]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "The new password must be at least 8 characters long.")]
            [DataType(DataType.Password)]
            public string NewPassword { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public class EmailChangeInput
        {
            [Required]
            [DataType(DataType.Password)]
            public string CurrentPassword { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string NewEmail { get; set; } = string.Empty;
        }

        public class OtpVerificationInput
        {
            [Required]
            [StringLength(6, MinimumLength = 6, ErrorMessage = "Enter the 6-digit verification code.")]
            [Display(Name = "Verification Code")]
            public string Code { get; set; } = string.Empty;
        }
    }
}
