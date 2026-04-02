using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class OfficeManagementModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public OfficeManagementModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public List<LguAccountViewModel> LguAccounts { get; set; } = new();

        [BindProperty]
        public string NewEmployeeId { get; set; } = string.Empty;

        [BindProperty]
        public string NewEmail { get; set; } = string.Empty;

        [BindProperty]
        public string NewDepartment { get; set; } = string.Empty;

        [BindProperty]
        public string NewPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadAccountsAsync();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            // Validate
            if (string.IsNullOrWhiteSpace(NewEmployeeId) ||
                string.IsNullOrWhiteSpace(NewEmail) ||
                string.IsNullOrWhiteSpace(NewDepartment) ||
                string.IsNullOrWhiteSpace(NewPassword))
            {
                ErrorMessage = "All fields are required.";
                await LoadAccountsAsync();
                return Page();
            }

            // Check if EmployeeId already exists
            var existing = _userManager.Users
                .FirstOrDefault(u => u.EmployeeId == NewEmployeeId);
            if (existing != null)
            {
                ErrorMessage = "An account with this Employee ID already exists.";
                await LoadAccountsAsync();
                return Page();
            }

            // Check if email already exists
            var existingEmail = await _userManager.FindByEmailAsync(NewEmail);
            if (existingEmail != null)
            {
                ErrorMessage = "An account with this email already exists.";
                await LoadAccountsAsync();
                return Page();
            }

            // Create the LGU account
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

            var result = await _userManager.CreateAsync(lguUser, NewPassword);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(lguUser, "LGU");
                SuccessMessage = $"LGU account for {NewDepartment} created successfully.";
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
                // Toggle lockout
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _userManager.SetLockoutEndDateAsync(user, null);
                    SuccessMessage = "Account re-enabled successfully.";
                }
                else
                {
                    await _userManager.SetLockoutEndDateAsync(
                        user, DateTimeOffset.UtcNow.AddYears(100));
                    SuccessMessage = "Account disabled successfully.";
                }
            }
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
                    CreatedAt = user.CreatedAt
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
    }
}