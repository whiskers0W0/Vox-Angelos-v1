// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("registration")]
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
        private readonly OcrService _ocrService;
        private readonly PrivateIdentityMediaStorage _privateIdentityMediaStorage;

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
            OcrService ocrService,
            PrivateIdentityMediaStorage privateIdentityMediaStorage)
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
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

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

            // Only required when IdType == "National ID" — checked in the handler rather
            // than with [Required], since every other ID type is single-sided.
            [Display(Name = "ID Photo (Back)")]
            public IFormFile? IdPhotoBack { get; set; }

            [Required]
            [Display(Name = "Selfie Photo")]
            public IFormFile SelfiePhoto { get; set; }

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

        // Mirrors the reason codes returned by /validate-id on the HF Space, mapped to a
        // specific, actionable message instead of relaying the Space's raw text verbatim —
        // keeps the citizen-facing wording consistent even if the Space's phrasing changes.
        private static string DescribeIdValidationFailure(string reasonCode, string fallbackReason, string sideLabel)
        {
            return reasonCode switch
            {
                "LOW_RESOLUTION" => $"Your {sideLabel} photo is too low-resolution. Please retake it in better lighting, closer to the ID.",
                "TOO_BLURRY" => $"Your {sideLabel} photo is too blurry. Hold the camera steady and make sure the ID is in focus.",
                "GLARE" => $"There's glare on your {sideLabel} photo. Tilt the ID slightly or move away from direct light and try again.",
                "NO_FACE" => $"We couldn't find a face on your {sideLabel} photo. Make sure you're photographing the side of the ID with your photo on it.",
                "OBSTRUCTED" => $"Part of your {sideLabel} photo looks covered or obscured. Make sure nothing (fingers, glare, shadows) is blocking the ID.",
                "TYPE_MISMATCH" => $"This doesn't look like the ID type you selected. Please double-check you're submitting the correct ID.",
                "WRONG_SIDE" => $"This looks like the wrong side of the ID. Please submit the {sideLabel} as requested.",
                _ => $"{sideLabel} ID Validation Failed: {fallbackReason}"
            };
        }

        // --- ADD THIS NEW HANDLER FOR STEP 1 VALIDATION ---
        public async Task<IActionResult> OnPostVerifyIdentityAsync(
            IFormFile idPhoto, IFormFile idPhotoBack, string idType, IFormFile selfiePhoto)
        {
            if (idPhoto == null || selfiePhoto == null)
            {
                return new JsonResult(new { success = false, error = "Both ID Photo and Live Selfie are required." });
            }

            if (string.IsNullOrWhiteSpace(idType))
            {
                return new JsonResult(new { success = false, error = "Please select an ID type." });
            }

            bool requiresBack = idType == "National ID";
            if (requiresBack && idPhotoBack == null)
            {
                return new JsonResult(new { success = false, error = "National ID requires a photo of the back as well." });
            }

            string tempIdPath = null;
            string tempIdBackPath = null;
            string tempSelfiePath = null;

            try
            {
                // Create a temporary folder for validation
                string tempFolder = Path.Combine(_environment.WebRootPath, "uploads", "temp");
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }

                // 1. Save temp ID photo (front)
                tempIdPath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(idPhoto.FileName)}");
                using (var stream = new FileStream(tempIdPath, FileMode.Create))
                {
                    await idPhoto.CopyToAsync(stream);
                }

                // 2. Validate the front of the ID
                var (isValidFront, frontReasonCode, frontReason) =
                    await _idValidationService.ValidateIdAsync(tempIdPath, idType, "front");
                if (!isValidFront)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = DescribeIdValidationFailure(frontReasonCode, frontReason, "front"),
                        reasonCode = frontReasonCode
                    });
                }

                // 2b. Save + validate the back, when this ID type needs one
                if (requiresBack)
                {
                    tempIdBackPath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(idPhotoBack.FileName)}");
                    using (var stream = new FileStream(tempIdBackPath, FileMode.Create))
                    {
                        await idPhotoBack.CopyToAsync(stream);
                    }

                    var (isValidBack, backReasonCode, backReason) =
                        await _idValidationService.ValidateIdAsync(tempIdBackPath, idType, "back");
                    if (!isValidBack)
                    {
                        return new JsonResult(new
                        {
                            success = false,
                            error = DescribeIdValidationFailure(backReasonCode, backReason, "back"),
                            reasonCode = backReasonCode
                        });
                    }
                }

                // 3. Save temp Selfie photo
                tempSelfiePath = Path.Combine(tempFolder, $"{Guid.NewGuid()}{Path.GetExtension(selfiePhoto.FileName)}");
                using (var stream = new FileStream(tempSelfiePath, FileMode.Create))
                {
                    await selfiePhoto.CopyToAsync(stream);
                }

                // 4. Verify Face Match — always against the front photo, since that's the
                // side with the citizen's photo on it for every supported ID type.
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
                if (tempIdBackPath != null && System.IO.File.Exists(tempIdBackPath)) System.IO.File.Delete(tempIdBackPath);
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

                // ── Save back-of-ID photo (National ID only) ────────────────────────
                string savedBackFileName = null;

                if (Input.IdPhotoBack != null && Input.IdPhotoBack.Length > 0)
                {
                    string uploadsFolder = IdentityDocumentStorage.IdsFolder(_environment);
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    string fileExtension = Path.GetExtension(Input.IdPhotoBack.FileName);
                    savedBackFileName = $"{Guid.NewGuid()}{fileExtension}";
                    string filePath = Path.Combine(uploadsFolder, savedBackFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                        await Input.IdPhotoBack.CopyToAsync(fileStream);
                }

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
                    PrivateIdentityMediaUpload privateIdBackUpload = null;
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

                    if (Input.IdPhotoBack != null && Input.IdPhotoBack.Length > 0)
                    {
                        try
                        {
                            privateIdBackUpload = await _privateIdentityMediaStorage
                                .UploadAsync(Input.IdPhotoBack, "id");
                        }
                        catch (Exception ex)
                        {
                            cloudUploadErrors.Add("Back-of-ID image backup failed.");
                            _logger.LogWarning(ex, "Private back-of-ID upload failed for user {UserId}.", user.Id);
                        }
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
    IdPhotoBackPath = savedBackFileName,
    IdPhotoBackCloudinaryPublicId = privateIdBackUpload?.PublicId,
    IdPhotoBackCloudinaryFormat = privateIdBackUpload?.Format,
    UploadedAt = DateTime.UtcNow,
};
_context.UserIdentityDocuments.Add(identityDocument);
await _context.SaveChangesAsync();

// ── Run OCR on the ID ───────────────────────────────────
// National ID prints the address/QR details on the back, not the front, so that's
// the side OCR needs to read for the residency gate below. Every other supported ID
// type is single-sided, so the front (its only side) is still what gets OCR'd.
string ocrTargetFileName = (Input.IdType == "National ID" && savedBackFileName != null)
    ? savedBackFileName
    : savedFileName;

if (ocrTargetFileName != null)
{
    string idPhotoFullPath = Path.Combine(IdentityDocumentStorage.IdsFolder(_environment), ocrTargetFileName);
    var ocrResult = await _ocrService.ExtractIdDataAsync(idPhotoFullPath);

    var ocrVerification = new UserOcrVerification
    {
        UserId = user.Id,
        IdentityDocumentId = identityDocument.Id,
        RawFullText = ocrResult.RawFullText,
        DetectedBirthDate = ocrResult.DetectedBirthDate,
        DetectedCardExpirationDate = ocrResult.DetectedCardExpiration,
        DetectedAddress = ocrResult.DetectedAddress,
        DetectedStreetAddress = ocrResult.DetectedStreetAddress,
        DetectedLocality = ocrResult.DetectedLocality,
        DetectedProvince = ocrResult.DetectedProvince,
        LocalityMatched = ocrResult.LocalityMatched,
        CityProvinceMatched = ocrResult.CityProvinceMatched,
        OcrConfidence = ocrResult.OcrConfidence,
        DetectionType = ocrResult.DetectionType,
        DetectedLanguageCode = ocrResult.DetectedLanguageCode ?? "en",
        ProcessedAt = DateTime.UtcNow
    };
    _context.UserOcrVerifications.Add(ocrVerification);

    // Keep the citizen profile limited to verified values.  The OCR service only
    // marks LocalityMatched when it identifies a supported Angeles City barangay,
    // so we never infer an address from unverified free text.
    if (DateOnly.TryParse(ocrResult.DetectedBirthDate, out var detectedBirthDate)
        && IsPlausibleBirthDate(detectedBirthDate))
    {
        profile.BirthDate = detectedBirthDate;
    }

    if (!string.IsNullOrWhiteSpace(ocrResult.DetectedStreetAddress))
    {
        profile.StreetAddress = ocrResult.DetectedStreetAddress;
    }

    if (ocrResult.LocalityMatched && !string.IsNullOrWhiteSpace(ocrResult.DetectedLocality))
    {
        profile.Barangay = ocrResult.DetectedLocality;
        profile.City = "Angeles City";
    }

    // The city/province match (not the barangay match) is the actual signal that this
    // is an Angeles City ID — a barangay name is easy to OCR-misread across 33 similar
    // options, while "Angeles City, Pampanga" printed on the card is a much more direct
    // and reliable check for whether this application belongs on the platform at all.
    if (ocrResult.CityProvinceMatched)
    {
        profile.Province = "Pampanga";
    }

    if (DateOnly.TryParse(ocrResult.DetectedCardExpiration, out var detectedCardExpiration))
    {
        identityDocument.CardExpirationDate = detectedCardExpiration;
    }

    await _context.SaveChangesAsync();

    _logger.LogInformation(
        "OCR completed for user {UserId}. LocalityMatched: {LocalityMatch} CityProvinceMatched: {CityMatch}",
        user.Id, ocrResult.LocalityMatched, ocrResult.CityProvinceMatched);

    // Vox Angelos is only for Angeles City, Pampanga residents — an ID that doesn't show
    // that city/province gets auto-rejected rather than left Pending, so it doesn't sit
    // in the admin queue looking like a normal application awaiting review. An admin can
    // still override this from Review Application if the OCR match was a false negative
    // (e.g. a blurry photo), same as any other rejection.
    if (!ocrResult.CityProvinceMatched)
    {
        user.ApprovalStatus = "Rejected";
        await _userManager.UpdateAsync(user);

        await _emailSender.SendEmailAsync(
            Input.Email,
            "Your Vox Angelos Account Application",
            $"<div style='font-family:Arial,sans-serif; max-width:480px; margin:0 auto;'>" +
            $"<h2 style='color:#c62828;'>Application Update</h2>" +
            $"<p>Hello,</p>" +
            $"<p>We regret to inform you that your Vox Angelos citizen account has been <strong style='color:#c62828;'>rejected</strong>.</p>" +
            $"<p><strong>Reason:</strong> We could not confirm that your ID shows an Angeles City, Pampanga address. Vox Angelos is currently only available to residents of Angeles City.</p>" +
            $"<p>If you believe this is an error, please contact support.</p>" +
            $"<p style='color:#888; font-size:0.85rem;'>— The Vox Angelos Team</p>" +
            $"</div>");

        _logger.LogInformation(
            "Auto-rejected citizen {UserId} — ID did not show an Angeles City, Pampanga address.", user.Id);
    }
}

                    // Face verification
                    bool isMatch = false;
                    decimal confidence = 0m;

                    if (savedFileName != null && savedSelfieFileName != null)
                    {
                        string idPhotoFullPathForFaceMatch = Path.Combine(IdentityDocumentStorage.IdsFolder(_environment), savedFileName);
                        string selfiePhotoFullPath = Path.Combine(IdentityDocumentStorage.SelfiesFolder(_environment), savedSelfieFileName);
                        (isMatch, confidence) = await _faceVerificationService.VerifyFacesAsync(idPhotoFullPathForFaceMatch, selfiePhotoFullPath);
                    }

                    var faceVerification = new UserFaceVerification
                    {
                        UserId = user.Id,
                        IdentityDocumentId = identityDocument.Id,
                        LiveSelfiePath = savedSelfieFileName,
                        LiveSelfieCloudinaryPublicId = privateSelfieUpload?.PublicId,
                        LiveSelfieCloudinaryFormat = privateSelfieUpload?.Format,
                        MatchConfidence = confidence,
                        VerificationStatus = isMatch ? "Verified" : "Failed",
                        VerifiedAt = DateTime.UtcNow,
                    };
                    _context.UserFaceVerifications.Add(faceVerification);
                    await _context.SaveChangesAsync();

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
