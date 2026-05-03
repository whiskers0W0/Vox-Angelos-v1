using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class OfficeManagementModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public const string DefaultLguPassword = "Lgu@123456";

        private static readonly HashSet<string> SeededEmails = new(StringComparer.OrdinalIgnoreCase)
        {
            "pptro@voxangelos.gov.ph",
            "osca@voxangelos.gov.ph",
            "pwdao@voxangelos.gov.ph",
            "mikaellagomez102004@gmail.com",
            "adrndgaming@gmail.com",
            "carlostannnn29+lgu@gmail.com",
            "alcuizargiogio+lgu@gmail.com"
        };

        public OfficeManagementModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public List<LguAccountViewModel> LguAccounts { get; set; } = new();

        [BindProperty] public string NewEmployeeId { get; set; } = string.Empty;
        [BindProperty] public string NewEmail { get; set; } = string.Empty;
        [BindProperty] public string NewDepartment { get; set; } = string.Empty;

        [BindProperty] public string EditUserId { get; set; } = string.Empty;
        [BindProperty] public string EditEmployeeId { get; set; } = string.Empty;
        [BindProperty] public string EditEmail { get; set; } = string.Empty;
        [BindProperty] public string EditPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadAccountsAsync();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            if (string.IsNullOrWhiteSpace(NewEmployeeId) ||
                string.IsNullOrWhiteSpace(NewEmail) ||
                string.IsNullOrWhiteSpace(NewDepartment))
            {
                ErrorMessage = "All fields are required.";
                await LoadAccountsAsync();
                return Page();
            }

            var existingEmpId = _userManager.Users.FirstOrDefault(u => u.EmployeeId == NewEmployeeId);
            if (existingEmpId != null)
            {
                ErrorMessage = "An account with this Employee ID already exists.";
                await LoadAccountsAsync();
                return Page();
            }

            var existingEmail = await _userManager.FindByEmailAsync(NewEmail);
            if (existingEmail != null)
            {
                ErrorMessage = "An account with this email already exists.";
                await LoadAccountsAsync();
                return Page();
            }

            var lguUser = new ApplicationUser
            {
                UserName = NewEmail,
                Email = NewEmail,
                EmailConfirmed = true,
                EmployeeId = NewEmployeeId,
                Department = NewDepartment,
                ApprovalStatus = "Approved",
                CreatedAt = DateTime.UtcNow,
                TwoFactorEnabled = false
            };

            var result = await _userManager.CreateAsync(lguUser, DefaultLguPassword);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(lguUser, "LGU");
                SuccessMessage = $"Account for {NewDepartment} created. Default password: {DefaultLguPassword}";
            }
            else
            {
                ErrorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
            }

            await LoadAccountsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _userManager.SetLockoutEndDateAsync(user, null);
                    SuccessMessage = "Account re-enabled successfully.";
                }
                else
                {
                    await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                    SuccessMessage = "Account disabled successfully.";
                }
            }
            await LoadAccountsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            if (string.IsNullOrWhiteSpace(EditUserId))
            {
                ErrorMessage = "Invalid account.";
                await LoadAccountsAsync();
                return Page();
            }

            var user = await _userManager.FindByIdAsync(EditUserId);
            if (user == null)
            {
                ErrorMessage = "Account not found.";
                await LoadAccountsAsync();
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(EditEmail) &&
                !string.Equals(EditEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var emailTaken = await _userManager.FindByEmailAsync(EditEmail);
                if (emailTaken != null && emailTaken.Id != EditUserId)
                {
                    ErrorMessage = "Email is already in use by another account.";
                    await LoadAccountsAsync();
                    return Page();
                }
                user.Email = EditEmail;
                user.NormalizedEmail = EditEmail.ToUpperInvariant();
                user.UserName = EditEmail;
                user.NormalizedUserName = EditEmail.ToUpperInvariant();
                user.EmailConfirmed = true;
            }

            if (!string.IsNullOrWhiteSpace(EditEmployeeId))
            {
                var empIdTaken = _userManager.Users.FirstOrDefault(u => u.EmployeeId == EditEmployeeId && u.Id != EditUserId);
                if (empIdTaken != null)
                {
                    ErrorMessage = "Employee ID is already in use by another account.";
                    await LoadAccountsAsync();
                    return Page();
                }
                user.EmployeeId = EditEmployeeId;
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                ErrorMessage = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                await LoadAccountsAsync();
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(EditPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var passResult = await _userManager.ResetPasswordAsync(user, token, EditPassword);
                if (!passResult.Succeeded)
                {
                    ErrorMessage = string.Join(", ", passResult.Errors.Select(e => e.Description));
                    await LoadAccountsAsync();
                    return Page();
                }
            }

            SuccessMessage = "Account updated successfully.";
            await LoadAccountsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostResetAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                ErrorMessage = "Account not found.";
                await LoadAccountsAsync();
                return Page();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, DefaultLguPassword);
            if (result.Succeeded)
                SuccessMessage = $"Password reset to default: {DefaultLguPassword}";
            else
                ErrorMessage = string.Join(", ", result.Errors.Select(e => e.Description));

            await LoadAccountsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                ErrorMessage = "Account not found.";
                await LoadAccountsAsync();
                return Page();
            }

            var label = user.Department ?? user.Email ?? "account";
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
                SuccessMessage = $"Account for {label} deleted successfully.";
            else
                ErrorMessage = string.Join(", ", result.Errors.Select(e => e.Description));

            await LoadAccountsAsync();
            return Page();
        }

        private async Task LoadAccountsAsync()
        {
            var lguUsers = await _userManager.GetUsersInRoleAsync("LGU");
            foreach (var user in lguUsers.OrderBy(u => u.Department))
            {
                LguAccounts.Add(new LguAccountViewModel
                {
                    UserId = user.Id,
                    EmployeeId = user.EmployeeId ?? "N/A",
                    Email = user.Email ?? "N/A",
                    Department = user.Department ?? "N/A",
                    IsActive = user.LockoutEnd == null || user.LockoutEnd < DateTimeOffset.UtcNow,
                    CreatedAt = user.CreatedAt,
                    IsSeeded = SeededEmails.Contains(user.Email ?? "")
                });
            }
        }
    }

    public class LguAccountViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsSeeded { get; set; }
    }
}
