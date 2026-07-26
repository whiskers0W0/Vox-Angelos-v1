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
            var idType = await _db.UserIdentityDocuments.AsNoTracking()
                .Where(d => d.UserId == user.Id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => d.IdType)
                .FirstOrDefaultAsync();
            var ocrVerification = await _db.UserOcrVerifications.AsNoTracking()
                .Where(verification => verification.UserId == user.Id)
                .OrderByDescending(verification => verification.ProcessedAt)
                .FirstOrDefaultAsync();

            DateOnly? detectedBirthDate = null;
            if (DateOnly.TryParse(ocrVerification?.DetectedBirthDate, out var parsedBirthDate)
                && IsPlausibleBirthDate(parsedBirthDate))
                detectedBirthDate = parsedBirthDate;

            var storedBirthDate = IsPlausibleBirthDate(profile?.BirthDate)
                ? profile!.BirthDate
                : null;

            var verifiedBarangay = ocrVerification?.LocalityMatched == true
                ? ocrVerification.DetectedLocality
                : null;
            var verifiedCity = ocrVerification?.LocalityMatched == true
                ? "Angeles City"
                : null;

            Profile = new CitizenProfileViewModel
            {
                FirstName = profile?.FirstName,
                MiddleName = profile?.MiddleName,
                LastName = profile?.LastName,
                PhoneNumber = user.PhoneNumber,
                Barangay = profile?.Barangay ?? verifiedBarangay,
                City = profile?.City ?? verifiedCity,
                EmailAddress = user.Email,
                BirthDate = storedBirthDate ?? detectedBirthDate,
                IdType = idType
            };

            return Page();
        }

        private static bool IsPlausibleBirthDate(DateOnly? value)
        {
            if (value == null) return false;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return value.Value >= new DateOnly(1900, 1, 1) && value.Value <= today;
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

            public string DisplayName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            public string VerificationSummary => string.IsNullOrWhiteSpace(IdType)
                ? "Identity verified"
                : $"Verified using {IdType}";
        }
    }
}
