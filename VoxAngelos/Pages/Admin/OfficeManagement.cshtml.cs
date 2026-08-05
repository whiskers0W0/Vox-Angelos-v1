using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class OfficeManagementModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public const string DefaultLguPassword = "Lgu@123456";

        public OfficeManagementModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public List<LguAccountViewModel> LguAccounts { get; set; } = new();

        // category -> department, across every LGU account — lets the Office Management
        // page grey out a category in the dropdown once another department already has it,
        // instead of only discovering the conflict after Save Changes.
        public Dictionary<string, string> CategoryOwnership { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [BindProperty] public string NewEmployeeId { get; set; } = string.Empty;
        [BindProperty] public string NewEmail { get; set; } = string.Empty;
        [BindProperty] public string NewDepartment { get; set; } = string.Empty;
        [BindProperty] public string NewDepartmentFullName { get; set; } = string.Empty;
        [BindProperty] public string NewTags { get; set; } = string.Empty;
        [BindProperty] public string NewCategories { get; set; } = string.Empty;

        [BindProperty] public string EditUserId { get; set; } = string.Empty;
        [BindProperty] public string EditEmployeeId { get; set; } = string.Empty;
        [BindProperty] public string EditEmail { get; set; } = string.Empty;
        [BindProperty] public string EditPassword { get; set; } = string.Empty;
        [BindProperty] public string EditDepartment { get; set; } = string.Empty;
        [BindProperty] public string EditDepartmentFullName { get; set; } = string.Empty;
        [BindProperty] public string EditTags { get; set; } = string.Empty;
        [BindProperty] public string EditCategories { get; set; } = string.Empty;

        // The fine-grained HF classifier categories available to assign, in the dropdown.
        public static readonly string[] AvailableCategories = ConcernClassificationService.Categories;

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
                string.IsNullOrWhiteSpace(NewDepartment) ||
                string.IsNullOrWhiteSpace(NewDepartmentFullName))
            {
                ErrorMessage = "All fields are required.";
                await LoadAccountsAsync();
                return Page();
            }

            NewDepartment = NewDepartment.Trim().ToUpperInvariant();

            var existingDept = _userManager.Users
                .FirstOrDefault(u => u.Department != null && u.Department.ToUpper() == NewDepartment);
            if (existingDept != null)
            {
                ErrorMessage = $"An LGU account already exists for {NewDepartment}. Each department can only have one account.";
                await LoadAccountsAsync();
                return Page();
            }

            var newCategories = ParseCategories(NewCategories);
            var categoryConflicts = FindCategoryConflicts(_userManager.Users.ToList(), newCategories);
            if (categoryConflicts.Count > 0)
            {
                ErrorMessage = "Cannot save — already assigned elsewhere: " +
                    string.Join(", ", categoryConflicts.Select(c => $"{c.Category} ({c.Department})"));
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
                DepartmentFullName = string.IsNullOrWhiteSpace(NewDepartmentFullName) ? null : NewDepartmentFullName.Trim(),
                Tags = ParseTags(NewTags),
                Categories = newCategories,
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
                IdentityResult result;
                if (await _userManager.IsLockedOutAsync(user))
                {
                    result = await _userManager.SetLockoutEndDateAsync(user, null);
                    if (result.Succeeded) SuccessMessage = "Account re-enabled successfully.";
                }
                else
                {
                    result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                    if (result.Succeeded) SuccessMessage = "Account disabled successfully.";
                }

                if (!result.Succeeded)
                    ErrorMessage = "Could not update this account — it may have just been changed by another admin.";
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

            if (!string.IsNullOrWhiteSpace(EditDepartment))
            {
                var normalizedDept = EditDepartment.Trim().ToUpperInvariant();
                if (!string.Equals(normalizedDept, user.Department, StringComparison.OrdinalIgnoreCase))
                {
                    var deptTaken = _userManager.Users
                        .FirstOrDefault(u => u.Department != null && u.Department.ToUpper() == normalizedDept && u.Id != EditUserId);
                    if (deptTaken != null)
                    {
                        ErrorMessage = $"An LGU account already exists for {normalizedDept}. Each department can only have one account.";
                        await LoadAccountsAsync();
                        return Page();
                    }
                }
                user.Department = normalizedDept;
            }

            if (!string.IsNullOrWhiteSpace(EditDepartmentFullName))
            {
                user.DepartmentFullName = EditDepartmentFullName.Trim();
            }

            var editCategories = ParseCategories(EditCategories);
            var categoryConflicts = FindCategoryConflicts(
                _userManager.Users.Where(u => u.Id != EditUserId).ToList(), editCategories);
            if (categoryConflicts.Count > 0)
            {
                ErrorMessage = "Cannot save — already assigned elsewhere: " +
                    string.Join(", ", categoryConflicts.Select(c => $"{c.Category} ({c.Department})"));
                await LoadAccountsAsync();
                return Page();
            }

            user.Tags = ParseTags(EditTags);
            user.Categories = editCategories;

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

        // A category can only ever be routed to one department at a time, so it can't be
        // assigned to a second account while another already claims it — otherwise
        // LoadCategoryDepartmentsAsync (the classifier's admin-mapping lookup) would have
        // two departments to choose from and silently pick whichever was read last.
        private static List<(string Category, string Department)> FindCategoryConflicts(
            IEnumerable<ApplicationUser> otherAccounts, List<string> categories)
        {
            var conflicts = new List<(string, string)>();
            foreach (var other in otherAccounts)
            {
                if (other.Categories == null || other.Categories.Count == 0) continue;
                foreach (var category in categories)
                {
                    if (other.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                        conflicts.Add((category, other.Department ?? other.Email ?? "another account"));
                }
            }
            return conflicts;
        }

        private static List<string> ParseTags(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
        }

        private static List<string> ParseCategories(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => ConcernClassificationService.Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
                    DepartmentFullName = user.DepartmentFullName ?? string.Empty,
                    Tags = user.Tags ?? new List<string>(),
                    Categories = user.Categories ?? new List<string>(),
                    IsActive = user.LockoutEnd == null || user.LockoutEnd < DateTimeOffset.UtcNow,
                    CreatedAt = user.CreatedAt
                });
            }

            CategoryOwnership.Clear();
            foreach (var account in LguAccounts)
                foreach (var category in account.Categories)
                    CategoryOwnership[category] = account.Department;
        }
    }

    public class LguAccountViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string DepartmentFullName { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
