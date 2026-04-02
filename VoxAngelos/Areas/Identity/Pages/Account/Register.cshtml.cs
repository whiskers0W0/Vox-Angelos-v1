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
            OcrService ocrService)
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
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

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

        // --- ADD THIS NEW HANDLER FOR STEP 1 VALIDATION ---
        public async Task<IActionResult> OnPostVerifyIdentityAsync(IFormFile idPhoto, IFormFile selfiePhoto)
        {
            if (idPhoto == null || selfiePhoto == null)
            {
                return new JsonResult(new { success = false, error = "Both ID Photo and Live Selfie are required." });
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
                var (isValidId, validationReason) = await _idValidationService.ValidateIdAsync(tempIdPath);
                if (!isValidId)
                {
                    return new JsonResult(new { success = false, error = $"ID Validation Failed: {validationReason}" });
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
                        //error = "Face verification failed. The selfie does not match the ID provided." });
                        error = $"Face verification failed. (Score: {confidence:F2}%). The selfie does not match the ID provided."
                    });
                }

                //// 4. Verify Face Match (UNCOMMENT TO BYPASS FOR TESTING)
                //var (isMatch, confidence) = await _faceVerificationService.VerifyFacesAsync(tempIdPath, tempSelfiePath);

                //// TESTING ONLY: Force pass regardless of match result — remove before production
                //bool isMatchOverride = true; // was: isMatch
                //if (!isMatchOverride)
                //{
                //    return new JsonResult(new
                //    {
                //        success = false,
                //        error = $"Face verification failed. (Score: {confidence:F2}%). The selfie does not match the ID provided."
                //    });
                //}

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
                var existingPhone = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == phone);

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

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // ── Duplicate phone check ──────────────────────────────────────────
                var existingUserWithPhone = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == Input.PhoneNumber);

                if (existingUserWithPhone != null)
                {
                    ModelState.AddModelError("Input.PhoneNumber",
                        "This phone number is already registered. Please use a different number.");
                    return Page();
                }

                // ── Save ID photo ──────────────────────────────────────────────────
                string savedFileName = null;

                if (Input.IdPhoto != null && Input.IdPhoto.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "ids");
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
                    string selfieFolder = Path.Combine(_environment.WebRootPath, "uploads", "selfies");
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
                    PhoneNumber = Input.PhoneNumber,
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
                    ModelState.AddModelError("Input.PhoneNumber",
                        "This phone number is already registered. Please use a different number.");
                    return Page();
                }

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

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
    UploadedAt = DateTime.UtcNow,
};
_context.UserIdentityDocuments.Add(identityDocument);
await _context.SaveChangesAsync();

// ── Run OCR on the ID photo ────────────────────────────
if (savedFileName != null)
{
    string idPhotoFullPath = Path.Combine(_environment.WebRootPath, "uploads", "ids", savedFileName);
    var ocrResult = await _ocrService.ExtractIdDataAsync(idPhotoFullPath);

    var ocrVerification = new UserOcrVerification
    {
        UserId = user.Id,
        IdentityDocumentId = identityDocument.Id,
        RawFullText = ocrResult.RawFullText,
        DetectedBirthDate = ocrResult.DetectedBirthDate,
        DetectedAddress = ocrResult.DetectedAddress,
        DetectedLocality = ocrResult.DetectedLocality,
        LocalityMatched = ocrResult.LocalityMatched,
        OcrConfidence = ocrResult.OcrConfidence,
        DetectionType = ocrResult.DetectionType,
        DetectedLanguageCode = ocrResult.DetectedLanguageCode ?? "en",
        ProcessedAt = DateTime.UtcNow
    };
    _context.UserOcrVerifications.Add(ocrVerification);
    await _context.SaveChangesAsync();

    _logger.LogInformation("OCR completed for user {UserId}. LocalityMatched: {Match}",
        user.Id, ocrResult.LocalityMatched);
}

                    // Face verification (bypassed for testing)
                    bool isMatch = true;
                    decimal confidence = 1.0m;

                    var faceVerification = new UserFaceVerification
                    {
                        UserId = user.Id,
                        IdentityDocumentId = identityDocument.Id,
                        LiveSelfiePath = savedSelfieFileName,
                        MatchConfidence = confidence,
                        VerificationStatus = isMatch ? "Verified" : "Failed",
                        VerifiedAt = DateTime.UtcNow,
                    };
                    _context.UserFaceVerifications.Add(faceVerification);
                    await _context.SaveChangesAsync();

                    await _userManager.AddToRoleAsync(user, "User");

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

                    return RedirectToPage("RegisterConfirmation", new
                    {
                        email = Input.Email,
                        returnUrl = returnUrl,
                        verified = isMatch,
                        confidence = confidence
                    });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
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