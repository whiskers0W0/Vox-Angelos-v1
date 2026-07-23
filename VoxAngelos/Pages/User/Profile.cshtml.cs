using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.User
{
    [Authorize(Roles = "User")]
    public class ProfileModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public CitizenProfileViewModel Profile { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var profile = await _db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);
            var document = await _db.UserIdentityDocuments.AsNoTracking()
                .Where(d => d.UserId == user.Id)
                .OrderByDescending(d => d.UploadedAt)
                .FirstOrDefaultAsync();

            Profile = new CitizenProfileViewModel
            {
                FirstName = profile?.FirstName,
                MiddleName = profile?.MiddleName,
                LastName = profile?.LastName,
                PhoneNumber = user.PhoneNumber,
                Barangay = profile?.Barangay,
                City = profile?.City,
                EmailAddress = user.Email,
                BirthDate = profile?.BirthDate,
                IdType = document?.IdType,
                IdDocumentPath = document?.IdPhotoPath
            };

            return Page();
        }

        public class CitizenProfileViewModel
        {
            public string? FirstName { get; set; }
            public string? MiddleName { get; set; }
            public string? LastName { get; set; }
            public string? PhoneNumber { get; set; }
            public string? Barangay { get; set; }
            public string? City { get; set; }
            public string? EmailAddress { get; set; }
            public DateOnly? BirthDate { get; set; }
            public string? IdType { get; set; }
            public string? IdDocumentPath { get; set; }

            public string DisplayName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
}
