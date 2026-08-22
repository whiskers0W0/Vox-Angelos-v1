// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly FaceVerificationService _faceVerificationService;
        private readonly IdValidationService _idValidationService;
        private readonly GeminiOcrService _ocrService;
        private readonly PrivateIdentityMediaStorage _privateIdentityMediaStorage;
        private readonly AwsFaceVerificationService _awsFaceVerificationService;
        private readonly RegistrationFaceTicketStore _faceTicketStore;
        private readonly FaceLivenessUsageGuard _faceLivenessUsageGuard;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            FaceVerificationService faceVerificationService,
            IdValidationService idValidationService,
            GeminiOcrService ocrService,
            PrivateIdentityMediaStorage privateIdentityMediaStorage,
            AwsFaceVerificationService awsFaceVerificationService,
            RegistrationFaceTicketStore faceTicketStore,
            FaceLivenessUsageGuard faceLivenessUsageGuard)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _environment = environment;
            _context = context;
            _faceVerificationService = faceVerificationService;
            _idValidationService = idValidationService;
            _ocrService = ocrService;
            _privateIdentityMediaStorage = privateIdentityMediaStorage;
            _awsFaceVerificationService = awsFaceVerificationService;
            _faceTicketStore = faceTicketStore;
            _faceLivenessUsageGuard = faceLivenessUsageGuard;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }
        public string AwsRegion => _awsFaceVerificationService.Region;
        public string AwsIdentityPoolId => _awsFaceVerificationService.IdentityPoolId;

        // Canonicalizes a PH mobile number to "+63XXXXXXXXXX" regardless of how it
        // arrives (bare 10 digits from the form field, "+63"-prefixed from the
        // Step 1 duplicate-check AJAX call, a leading 0, stray spaces/dashes, etc.)
        // — without this, the same number could be stored/compared in different
        // shapes across call sites, silently defeating the uniqueness check.
        private static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return phone;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("63")) digits = digits.Substring(2);
            else if (digits.StartsWith("0")) digits = digits.Substring(1);
            return "+63" + digits;
        }

        private static bool IsPlausibleBirthDate(DateOnly value)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return value >= new DateOnly(1900, 1, 1) && value <= today;
        }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Display(Name = "Middle Name")]
            public string MiddleName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Required]
            [Phone]
            [Display(Name = "Phone Number")]
            public string PhoneNumber { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [Display(Name = "ID Type")]
            public string IdType { get; set; }

            [Required]
            [Display(Name = "ID Photo")]
            public IFormFile IdPhoto { get; set; }

            [Display(Name = "Selfie Photo")]
            public IFormFile SelfiePhoto { get; set; }

            [Required]
            public string FaceVerificationToken { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [Required]
            [DataType(DataType.Password)]
            [Display(Name = "Confirm Password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        private static async Task<(byte[] Bytes, string Hash)> ReadAndHashAsync(IFormFile file)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            return (bytes, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        public async Task<IActionResult> OnPostStartFaceLivenessAsync()
        {
            if (string.IsNullOrWhiteSpace(_awsFaceVerificationService.IdentityPoolId))
                return new JsonResult(new { success = false, error = "Face liveness is not configured." }) { StatusCode = 503 };

            var clientKey = GetFaceLivenessClientKey();
            var limit = _faceLivenessUsageGuard.TryBegin(clientKey, GetFaceLivenessIpKey());
            if (!limit.Allowed)
            {
                Response.Headers.RetryAfter = limit.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return new JsonResult(new { success = false, error = limit.Error }) { StatusCode = 429 };
            }
            try
            {
                var sessionId = await _awsFaceVerificationService.CreateLivenessSessionAsync(HttpContext.RequestAborted);
                return new JsonResult(new { success = true, sessionId });
            }
            catch (Exception ex)
            {
                _faceLivenessUsageGuard.End(clientKey);
                _logger.LogError(ex, "Could not create AWS face liveness session.");
                return new JsonResult(new { success = false, error = "The liveness service is temporarily unavailable." }) { StatusCode = 503 };
            }
        }

        public IActionResult OnPostCancelFaceLiveness()
        {
            // Releases only the short-lived "currently active" lock. The attempt
            // remains counted because AWS already created a paid liveness session.
            _faceLivenessUsageGuard.End(GetFaceLivenessClientKey());
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostCompleteFaceLivenessAsync(
            IFormFile idPhoto, string idType, string sessionId)
        {
            if (idPhoto is null || string.IsNullOrWhiteSpace(idType) || string.IsNullOrWhiteSpace(sessionId))
                return new JsonResult(new { success = false, error = "ID photo, ID type, and liveness session are required." }) { StatusCode = 400 };

            string tempIdPath = null;
            try
            {
                var (idBytes, idHash) = await ReadAndHashAsync(idPhoto);
                var tempFolder = Path.Combine(_environment.WebRootPath, "uploads", "temp");
                Directory.CreateDirectory(tempFolder);
                tempIdPath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(idPhoto.FileName)}");
                await System.IO.File.WriteAllBytesAsync(tempIdPath, idBytes);

                var (isValidId, reasonCode, reason) = await _idValidationService.ValidateIdAsync(tempIdPath, idType);
                if (!isValidId)
                    return new JsonResult(new { success = false, error = DescribeIdValidationFailure(reasonCode, reason), reasonCode });

                var result = await _awsFaceVerificationService.VerifyAsync(idBytes, sessionId, HttpContext.RequestAborted);
                if (!result.IsMatch || result.ReferenceImage is null)
                    return new JsonResult(new
                    {
                        success = false,
                        error = result.Error ?? "Face verification failed."
                    });

                var token = _faceTicketStore.Create(new RegistrationFaceTicket(
                    idHash,
                    result.ReferenceImage,
                    (decimal)(result.LivenessConfidence / 100f),
                    (decimal)(result.Similarity / 100f),
                    DateTimeOffset.UtcNow.AddMinutes(15)));

                return new JsonResult(new
                {
                    success = true,
                    verificationToken = token
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AWS face verification failed.");
                return new JsonResult(new { success = false, error = "Identity verification is temporarily unavailable. Please retry." }) { StatusCode = 503 };
            }
            finally
            {
                _faceLivenessUsageGuard.End(GetFaceLivenessClientKey());
                if (!string.IsNullOrWhiteSpace(tempIdPath) && System.IO.File.Exists(tempIdPath))
                    System.IO.File.Delete(tempIdPath);
            }
        }

        private string GetFaceLivenessClientKey()
        {
            const string cookieName = "VoxAngelos.FaceClient";
            var clientId = Request.Cookies[cookieName];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                Response.Cookies.Append(cookieName, clientId, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(1),
                    IsEssential = true
                });
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)));
        }

        private string GetFaceLivenessIpKey()
        {
            // ForwardedHeadersMiddleware replaces RemoteIpAddress with the original
            // client address from Render's trusted nearest proxy hop. Hash it so the
            // citizen's raw address is never retained in application memory.
            var address = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));
        }
        // Mirrors the reason codes returned by /validate-id on the HF Space, mapped to a
        // specific, actionable message instead of relaying the Space's raw text verbatim —
        // keeps the citizen-facing wording consistent even if the Space's phrasing changes.
        private static string DescribeIdValidationFailure(string reasonCode, string fallbackReason)
        {
            return reasonCode switch
            {
                "LOW_RESOLUTION" => "Your ID photo is too low-resolution. Please retake it in better lighting, closer to the ID.",
                "TOO_BLURRY" => "Your ID photo is too blurry. Hold the camera steady and make sure the ID is in focus.",
                "GLARE" => "There's glare on your ID photo. Tilt the ID slightly or move away from direct light and try again.",
                "NO_FACE" => "We couldn't find a face on your ID photo. Make sure you're photographing the side of the ID with your photo on it.",
                "OBSTRUCTED" => "Part of your ID photo looks covered or obscured. Make sure nothing (fingers, glare, shadows) is blocking the ID.",
                "TYPE_MISMATCH" => "This doesn't look like the ID type you selected. Please double-check you're submitting the correct ID.",
                _ => $"ID Validation Failed: {fallbackReason}"
            };
        }

        // --- ADD THIS NEW HANDLER FOR STEP 1 VALIDATION ---
        public async Task<IActionResult> OnPostVerifyIdentityAsync(
            IFormFile idPhoto, string idType, IFormFile selfiePhoto)
        {
            if (idPhoto == null || selfiePhoto == null)
            {
                return new JsonResult(new { success = false, error = "Both ID Photo and Live Selfie are required." });
            }

            if (string.IsNullOrWhiteSpace(idType))
            {
                return new JsonResult(new { success = false, error = "Please select an ID type." });
            }

            string tempIdPath = null;
            string tempSelfiePath = null;

            try
            {
                // Create a temporary folder for validation
                string tempFolder = Path.Combine(_environment.WebRootPath, "uploads", "temp");
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }

                // 1. Save temp ID photo
                tempIdPath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(idPhoto.FileName)}");
                using (var stream = new FileStream(tempIdPath, FileMode.Create))
                {
                    await idPhoto.CopyToAsync(stream);
                }

                // 2. Validate the ID Document
                var (isValidId, reasonCode, reason) =
                    await _idValidationService.ValidateIdAsync(tempIdPath, idType);
                if (!isValidId)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = DescribeIdValidationFailure(reasonCode, reason),
                        reasonCode
                    });
                }

                // 3. Save temp Selfie photo
                tempSelfiePath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(selfiePhoto.FileName)}");
                using (var stream = new FileStream(tempSelfiePath, FileMode.Create))
                {
                    await selfiePhoto.CopyToAsync(stream);
                }

                // 4. Verify Face Match
                var (isMatch, confidence) = await _faceVerificationService.VerifyFacesAsync(tempIdPath, tempSelfiePath);
                if (!isMatch)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = $"Face verification failed. (Score: {confidence:F2}%). The selfie does not match the ID provided."
                    });
                }

                // All checks passed: return success JSON so all code paths return a value
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying identity in Step 2");
                return new JsonResult(new { success = false, error = "A server error occurred during verification. Please try again." });
            }
            finally
            {
                // Clean up temporary files immediately to save space
                if (tempIdPath != null && System.IO.File.Exists(tempIdPath)) System.IO.File.Delete(tempIdPath);
                if (tempSelfiePath != null && System.IO.File.Exists(tempSelfiePath)) System.IO.File.Delete(tempSelfiePath);
            }
        }
        // --------------------------------------------------
        public async Task<IActionResult> OnGetCheckDuplicatesAsync(string email, string phone)
        {
            var errors = new Dictionary<string, string>();

            // 1. Check for duplicate Email
            if (!string.IsNullOrWhiteSpace(email))
            {
                var existingEmail = await _userManager.FindByEmailAsync(email);
                if (existingEmail != null)
                {
                    errors.Add("Input_Email", "This email is already registered.");
                }
            }

            // 2. Check for duplicate Phone Number
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var normalizedPhone = NormalizePhone(phone);
                var existingPhone = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);

                if (existingPhone != null)
                {
                    errors.Add("Input_PhoneNumber", "This phone number is already registered.");
                }
            }

            // Return results
            if (errors.Any())
            {
                return new JsonResult(new { success = false, errors });
            }

            return new JsonResult(new { success = true });
        }
        // --------------------------------------------------
        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        // AJAX handler for the final step's "Create Account" submission — kept as a JSON
        // endpoint (instead of a native Page() postback) so a validation failure (e.g. a
        // rejected password) never triggers a full page reload. A full reload would reset
        // the client-side wizard to Step 1 and silently drop the already-selected ID/selfie
        // files, since browsers refuse to preserve <input type="file"> values across
        // navigations. Staying on the same page means the user only has to fix the one
        // field that failed.
        public async Task<IActionResult> OnPostCreateAccountAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            RegistrationFaceTicket faceTicket = null;
            if (Input?.IdPhoto is not null
                && !string.IsNullOrWhiteSpace(Input.FaceVerificationToken)
                && _faceTicketStore.TryGet(Input.FaceVerificationToken, out faceTicket))
            {
                var (_, submittedIdHash) = await ReadAndHashAsync(Input.IdPhoto);
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(faceTicket.IdImageHash),
                    Convert.FromHexString(submittedIdHash)))
                {
                    faceTicket = null;
                }
            }

            if (faceTicket is null)
            {
                ModelState.AddModelError(string.Empty,
                    "Your face verification has expired or does not match this ID. Please complete it again.");
            }
            else
            {
                Input.SelfiePhoto = new FormFile(new MemoryStream(faceTicket.ReferenceImage), 0,
                    faceTicket.ReferenceImage.Length, "Input.SelfiePhoto", "aws-liveness-reference.jpg")
                { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
            }

            if (ModelState.IsValid)
            {
                // ── Duplicate phone check ──────────────────────────────────────────
                var normalizedPhone = NormalizePhone(Input.PhoneNumber);
                var existingUserWithPhone = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);

                if (existingUserWithPhone != null)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        fieldErrors = new Dictionary<string, string>
                        {
                            ["Input_PhoneNumber"] = "This phone number is already registered. Please use a different number."
                        }
                    });
                }

                // ── Save ID photo ──────────────────────────────────────────────────
                string savedFileName = null;

                if (Input.IdPhoto != null && Input.IdPhoto.Length > 0)
                {
                    string uploadsFolder = IdentityDocumentStorage.IdsFolder(_environment);
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    string fileExtension = Path.GetExtension(Input.IdPhoto.FileName);
                    savedFileName = $"{Guid.NewGuid()}{fileExtension}";
                    string filePath = Path.Combine(uploadsFolder, savedFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                        await Input.IdPhoto.CopyToAsync(fileStream);
                }
                // ← this brace closes ONLY the IdPhoto if-block, NOT ModelState.IsValid

                // ── Save selfie photo ──────────────────────────────────────────────
                string savedSelfieFileName = null;

                if (Input.SelfiePhoto != null && Input.SelfiePhoto.Length > 0)
                {
                    string selfieFolder = IdentityDocumentStorage.SelfiesFolder(_environment);
                    if (!Directory.Exists(selfieFolder))
                        Directory.CreateDirectory(selfieFolder);

                    string selfieExtension = Path.GetExtension(Input.SelfiePhoto.FileName);
                    savedSelfieFileName = $"{Guid.NewGuid()}{selfieExtension}";
                    string selfiePath = Path.Combine(selfieFolder, savedSelfieFileName);

                    using (var fileStream = new FileStream(selfiePath, FileMode.Create))
                        await Input.SelfiePhoto.CopyToAsync(fileStream);
                }

                // ── Create the ApplicationUser ─────────────────────────────────────
                var user = new ApplicationUser
                {
                    UserName = Input.Email,
                    Email = Input.Email,
                    PhoneNumber = normalizedPhone,
                    TwoFactorEnabled = true,
                    ApprovalStatus = "Pending",
                    CreatedAt = DateTime.UtcNow
                };

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                IdentityResult result;
                try
                {
                    result = await _userManager.CreateAsync(user, Input.Password);
                }
                catch (Exception ex) when (
                    ex.InnerException?.Message.Contains("PhoneNumber") == true ||
                    ex.InnerException?.Message.Contains("IX_AspNetUsers_PhoneNumber") == true ||
                    ex.InnerException?.Message.Contains("UNIQUE") == true)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        fieldErrors = new Dictionary<string, string>
                        {
                            ["Input_PhoneNumber"] = "This phone number is already registered. Please use a different number."
                        }
                    });
                }

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // Upload the original request files immediately after account creation,
                    // before OCR and face matching perform additional work. This mirrors the
                    // working concern/recommendation attachment flow while keeping identity
                    // media private. Protected local copies remain available for retries.
                    PrivateIdentityMediaUpload privateIdUpload = null;
                    PrivateIdentityMediaUpload privateSelfieUpload = null;
                    var cloudUploadErrors = new List<string>();

                    try
                    {
                        privateIdUpload = await _privateIdentityMediaStorage
                            .UploadAsync(Input.IdPhoto, "id");
                    }
                    catch (Exception ex)
                    {
                        cloudUploadErrors.Add("ID image backup failed.");
                        _logger.LogWarning(ex, "Private ID upload failed for user {UserId}.", user.Id);
                    }

                    try
                    {
                        privateSelfieUpload = await _privateIdentityMediaStorage
                            .UploadAsync(Input.SelfiePhoto, "selfie");
                    }
                    catch (Exception ex)
                    {
                        cloudUploadErrors.Add("Selfie backup failed.");
                        _logger.LogWarning(ex, "Private selfie upload failed for user {UserId}.", user.Id);
                    }

                    // Save UserProfile
                    var profile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                    };
                    _context.UserProfiles.Add(profile);

                    // Save UserIdentityDocument
                    // Save UserIdentityDocument
var identityDocument = new UserIdentityDocument
{
    UserId = user.Id,
    IdType = Input.IdType,
    IdPhotoPath = savedFileName,
    IdPhotoCloudinaryPublicId = privateIdUpload?.PublicId,
    IdPhotoCloudinaryFormat = privateIdUpload?.Format,
    UploadedAt = DateTime.UtcNow,
};
_context.UserIdentityDocuments.Add(identityDocument);
await _context.SaveChangesAsync();

// ── Run OCR on the ID photo ────────────────────────────
if (savedFileName != null)
{
    string idPhotoFullPath = Path.Combine(IdentityDocumentStorage.IdsFolder(_environment), savedFileName);
    var ocrResult = await _ocrService.ExtractIdDataAsync(idPhotoFullPath);

    var ocrVerification = new UserOcrVerification
    {
        UserId = user.Id,
        IdentityDocumentId = identityDocument.Id,
        RawFullText = ocrResult.RawFullText,
        DetectedFirstName = ocrResult.DetectedFirstName,
        DetectedMiddleName = ocrResult.DetectedMiddleName,
        DetectedLastName = ocrResult.DetectedLastName,
        DetectedBirthDate = ocrResult.DetectedBirthDate,
        DetectedCardExpirationDate = ocrResult.DetectedCardExpiration,
        DetectedAddress = ocrResult.DetectedAddress,
        CityProvinceMatched = ocrResult.CityProvinceMatched,
        OcrConfidence = ocrResult.OcrConfidence,
        DetectionType = ocrResult.DetectionType,
        DetectedLanguageCode = ocrResult.DetectedLanguageCode ?? "en",
        ProcessedAt = DateTime.UtcNow
    };
    _context.UserOcrVerifications.Add(ocrVerification);

    // Keep the citizen profile limited to verified values — the address is a single
    // detected string now (different ID types format it too inconsistently to split),
    // so it only gets attributed to the profile once the city/province check passes.
    if (DateOnly.TryParseExact(ocrResult.DetectedBirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var detectedBirthDate)
        && IsPlausibleBirthDate(detectedBirthDate))
    {
        profile.BirthDate = detectedBirthDate;
    }

    // The city/province match is the actual signal that this is an Angeles City ID —
    // "Angeles City, Pampanga" printed on the card is a direct, reliable check for
    // whether this application belongs on the platform at all.
    if (ocrResult.CityProvinceMatched)
    {
        profile.StreetAddress = ocrResult.DetectedAddress;
        profile.City = "Angeles City";
        profile.Province = "Pampanga";
    }

    if (DateOnly.TryParseExact(ocrResult.DetectedCardExpiration, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var detectedCardExpiration))
    {
        identityDocument.CardExpirationDate = detectedCardExpiration;
    }

    await _context.SaveChangesAsync();

    _logger.LogInformation(
        "OCR completed for user {UserId}. CityProvinceMatched: {CityMatch}",
        user.Id, ocrResult.CityProvinceMatched);

    // No auto-approve/reject here — every application is left "Pending" and goes
    // through manual admin review on the Review Application page, which shows the
    // OCR-detected fields (including the Angeles City match) for the admin to decide.
}

                    // Face verification
                    bool isMatch = false;
                    decimal confidence = 0m;

                    if (savedFileName != null && savedSelfieFileName != null)
                    {
                        string idPhotoFullPathForFaceMatch = Path.Combine(IdentityDocumentStorage.IdsFolder(_environment), savedFileName);
                        string selfiePhotoFullPath = Path.Combine(IdentityDocumentStorage.SelfiesFolder(_environment), savedSelfieFileName);
                        isMatch = true;
                        // The registration ticket has already normalized Rekognition's
                        // 0-100 similarity to the application's canonical 0-1 scale.
                        confidence = Math.Clamp(faceTicket.Similarity, 0m, 1m);
                    }

                    var faceVerification = new UserFaceVerification
                    {
                        UserId = user.Id,
                        IdentityDocumentId = identityDocument.Id,
                        LiveSelfiePath = savedSelfieFileName,
                        LiveSelfieCloudinaryPublicId = privateSelfieUpload?.PublicId,
                        LiveSelfieCloudinaryFormat = privateSelfieUpload?.Format,
                        LivenessConfidence = Math.Clamp(faceTicket.LivenessConfidence, 0m, 1m),
                        MatchConfidence = confidence,
                        VerificationStatus = isMatch ? "Verified" : "Failed",
                        VerifiedAt = DateTime.UtcNow,
                    };
                    _context.UserFaceVerifications.Add(faceVerification);
                    await _context.SaveChangesAsync();
                    _faceTicketStore.Remove(Input.FaceVerificationToken);

                    if (cloudUploadErrors.Count == 0)
                    {
                        identityDocument.CloudinaryUploadAttempts = 0;
                        identityDocument.CloudinaryNextRetryAt = null;
                        identityDocument.CloudinaryLastUploadError = null;
                    }
                    else
                    {
                        identityDocument.CloudinaryUploadAttempts = 1;
                        identityDocument.CloudinaryNextRetryAt = DateTime.UtcNow.AddMinutes(5);
                        identityDocument.CloudinaryLastUploadError = string.Join(" ", cloudUploadErrors);
                    }

                    await _context.SaveChangesAsync();

                    await _userManager.AddToRoleAsync(user, "User");

                    if (user.ApprovalStatus == "Pending")
                        await NotifyAdminsOfPendingApplicationAsync(user, profile);

                    // Send email confirmation
                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                    var redirectUrl = Url.Page("RegisterConfirmation", new
                    {
                        email = Input.Email,
                        returnUrl = returnUrl,
                        verified = isMatch,
                        confidence = confidence
                    });

                    return new JsonResult(new { success = true, redirectUrl });
                }

                return new JsonResult(new
                {
                    success = false,
                    generalErrors = result.Errors.Select(e => e.Description).ToList()
                });
            }

            // ModelState was invalid from data annotations (e.g. password too short) —
            // surface each failing field by its generated element id (Input.Password -> Input_Password)
            // so the client can show the message without navigating away from the current step.
            var fieldErrors = ModelState
                .Where(kvp => kvp.Value.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key.Replace(".", "_"),
                    kvp => string.Join(" ", kvp.Value.Errors.Select(e => e.ErrorMessage)));

            return new JsonResult(new { success = false, fieldErrors });
        }


        private async Task NotifyAdminsOfPendingApplicationAsync(ApplicationUser applicant, UserProfile profile)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            var applicantName = $"{profile.FirstName} {profile.LastName}".Trim();
            foreach (var admin in admins.Where(admin => admin.LockoutEnd == null || admin.LockoutEnd < DateTimeOffset.UtcNow))
            {
                _context.UserNotifications.Add(new UserNotification
                {
                    RecipientUserId = admin.Id,
                    Title = "New citizen application",
                    Message = $"{applicantName} submitted an account application for review.",
                    NotificationType = "CitizenApplication",
                    SenderRole = "Citizen",
                    SenderName = applicantName,
                    LinkUrl = $"/Admin/ReviewApplication?userId={Uri.EscapeDataString(applicant.Id)}",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();

            foreach (var admin in admins.Where(admin => admin.EmailNotificationsEnabled && admin.EmailConfirmed && !string.IsNullOrWhiteSpace(admin.Email)))
            {
                try
                {
                    await _emailSender.SendEmailAsync(admin.Email!, "New Vox Angelos citizen application",
                        $"<p><strong>{HtmlEncoder.Default.Encode(applicantName)}</strong> submitted a citizen account application.</p><p>Sign in to the Admin portal to review the identity-verification results and application evidence.</p>");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Admin application alert email failed for {AdminId}.", admin.Id);
                }
            }
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor.");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }

            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
