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
        private static readonly IReadOnlyDictionary<string, string> AllowedAnimalAvatars =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["philippine-eagle"] = "/images/avatars/philippine-eagle.png",
                ["philippine-tarsier"] = "/images/avatars/philippine-tarsier.png",
                ["carabao"] = "/images/avatars/carabao.png",
                ["pawikan"] = "/images/avatars/pawikan.png",
                ["owl"] = "/images/avatars/owl.png",
                ["aspin"] = "/images/avatars/aspin.png",
                ["puspin"] = "/images/avatars/puspin.png",
                ["rabbit"] = "/images/avatars/rabbit.png",
                ["dolphin"] = "/images/avatars/dolphin.png",
                ["philippine-hornbill"] = "/images/avatars/philippine-hornbill.png"
            };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public CitizenProfileViewModel Profile { get; private set; } = new();

        [BindProperty]
        public string? SelectedAvatar { get; set; }

        public IReadOnlyList<AvatarOptionViewModel> AvatarOptions { get; } =
            new List<AvatarOptionViewModel>
            {
                new("philippine-eagle", "Philippine Eagle", "/images/avatars/philippine-eagle.png"),
                new("philippine-tarsier", "Philippine Tarsier", "/images/avatars/philippine-tarsier.png"),
                new("carabao", "Carabao", "/images/avatars/carabao.png"),
                new("pawikan", "Pawikan", "/images/avatars/pawikan.png"),
                new("owl", "Owl", "/images/avatars/owl.png"),
                new("aspin", "Aspin", "/images/avatars/aspin.png"),
                new("puspin", "Puspin", "/images/avatars/puspin.png"),
                new("rabbit", "Rabbit", "/images/avatars/rabbit.png"),
                new("dolphin", "Dolphin", "/images/avatars/dolphin.png"),
                new("philippine-hornbill", "Philippine Hornbill", "/images/avatars/philippine-hornbill.png")
            };

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            var profile = await _db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);
            var latestIdentityDocument = await _db.UserIdentityDocuments.AsNoTracking()
                .Where(d => d.UserId == user.Id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new { d.IdType, d.CardExpirationDate })
                .FirstOrDefaultAsync();
            var idType = latestIdentityDocument?.IdType;
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
            var verifiedProvince = ocrVerification?.CityProvinceMatched == true
                ? "Pampanga"
                : null;

            Profile = new CitizenProfileViewModel
            {
                FirstName = profile?.FirstName,
                MiddleName = profile?.MiddleName,
                LastName = profile?.LastName,
                PhoneNumber = user.PhoneNumber,
                StreetAddress = profile?.StreetAddress ?? ocrVerification?.DetectedStreetAddress,
                Barangay = profile?.Barangay ?? verifiedBarangay,
                City = profile?.City ?? verifiedCity,
                Province = profile?.Province ?? verifiedProvince,
                EmailAddress = user.Email,
                BirthDate = storedBirthDate ?? detectedBirthDate,
                IdType = idType,
                CardExpirationDate = latestIdentityDocument?.CardExpirationDate,
                ProfilePhotoUrl = user.ProfilePhotoUrl
            };

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateAvatarAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Challenge();

            if (string.IsNullOrWhiteSpace(SelectedAvatar)
                || !AllowedAnimalAvatars.TryGetValue(SelectedAvatar, out var avatarUrl))
            {
                TempData["ProfileAvatarError"] = "Please choose one of the available animal avatars.";
                return RedirectToPage();
            }

            user.ProfilePhotoUrl = avatarUrl;
            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                TempData["ProfileAvatarError"] = "Your avatar could not be updated. Please try again.";
                return RedirectToPage();
            }

            TempData["ProfileAvatarSuccess"] = "Your profile avatar has been updated.";
            return RedirectToPage();
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
            public string? StreetAddress { get; set; }
            public string? Barangay { get; set; }
            public string? City { get; set; }
            public string? Province { get; set; }
            public string? EmailAddress { get; set; }
            public DateOnly? BirthDate { get; set; }
            public string? IdType { get; set; }
            public DateOnly? CardExpirationDate { get; set; }
            public string? ProfilePhotoUrl { get; set; }

            public string DisplayName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            public string VerificationSummary => string.IsNullOrWhiteSpace(IdType)
                ? "Identity verified"
                : $"Verified using {IdType}";

            // Computed from the scanned birthday rather than stored, so it's always
            // current as of the day the profile is viewed.
            public int? Age => BirthDate == null ? null : ComputeAge(BirthDate.Value);

            private static int ComputeAge(DateOnly birthDate)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var age = today.Year - birthDate.Year;
                if (birthDate > today.AddYears(-age)) age--;
                return age;
            }
        }

        public record AvatarOptionViewModel(string Key, string Name, string ImageUrl);
    }
}
