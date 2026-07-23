using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class ReviewApplicationModel : PageModel
    {
        private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ReviewApplicationModel> _logger;

        public ReviewApplicationModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IWebHostEnvironment environment,
            ILogger<ReviewApplicationModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _environment = environment;
            _logger = logger;
        }

        public ApplicationUser? Citizen { get; set; }
        public UserProfile? Profile { get; set; }
        public UserIdentityDocument? IdentityDocument { get; set; }
        public UserFaceVerification? FaceVerification { get; set; }
        public UserOcrVerification? OcrVerification { get; set; }

        public async Task<IActionResult> OnGetAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return RedirectToPage("/Admin/UserApplications");

            Citizen = await _userManager.FindByIdAsync(userId);
            if (Citizen == null)
                return RedirectToPage("/Admin/UserApplications");

            Profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);

            IdentityDocument = await _context.UserIdentityDocuments
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.UploadedAt)
                .FirstOrDefaultAsync();

            if (IdentityDocument != null)
            {
                FaceVerification = await _context.UserFaceVerifications
                    .FirstOrDefaultAsync(f => f.IdentityDocumentId == IdentityDocument.Id);

                OcrVerification = await _context.UserOcrVerifications
                    .FirstOrDefaultAsync(o => o.IdentityDocumentId == IdentityDocument.Id);
            }

            return Page();
        }

        // Serves the private ID photo / live selfie for a single application. The class-level
        // [Authorize(Policy = "RequireAdminRole")] above covers this handler too, so citizens,
        // LGU accounts, and logged-out requests are rejected before any file lookup happens.
        // Files live under App_Data (outside wwwroot), so this handler is the only way to
        // read them — there is no public URL that serves them directly.
        public async Task<IActionResult> OnGetIdentityMediaAsync(int documentId, string mediaType)
        {
            string? fileName;
            string folder;

            if (mediaType == "id")
            {
                var doc = await _context.UserIdentityDocuments.FindAsync(documentId);
                fileName = doc?.IdPhotoPath;
                folder = IdentityDocumentStorage.IdsFolder(_environment);
            }
            else if (mediaType == "selfie")
            {
                var face = await _context.UserFaceVerifications
                    .FirstOrDefaultAsync(f => f.IdentityDocumentId == documentId);
                fileName = face?.LiveSelfiePath;
                folder = IdentityDocumentStorage.SelfiesFolder(_environment);
            }
            else
            {
                return BadRequest();
            }

            // Covers both "never uploaded" and "purged after the retention window" (the
            // path field is cleared to null by SensitiveMediaRetentionService) — either way
            // the page's existing "No photo" / "No selfie" placeholder is what should show.
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
                return NotFound();

            var fullPath = Path.Combine(folder, fileName);
            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            if (!ContentTypeProvider.TryGetContentType(fullPath, out var contentType))
                contentType = "application/octet-stream";

            return PhysicalFile(fullPath, contentType);
        }

        public async Task<IActionResult> OnPostApproveAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.ApprovalStatus != "Approved")
            {
                user.ApprovalStatus = "Approved";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    TempData["AdminError"] = "Could not approve this application — it may have just been changed by another admin.";
                    return RedirectToPage("/Admin/UserApplications");
                }

                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Your Vox Angelos Account Has Been Approved",
                    $"<div style='font-family:Arial,sans-serif; max-width:480px; margin:0 auto;'>" +
                    $"<h2 style='color:#1a237e;'>Account Approved!</h2>" +
                    $"<p>Hello,</p>" +
                    $"<p>Great news! Your <strong>Vox Angelos</strong> citizen account has been reviewed and <strong style='color:#2e7d32;'>approved</strong>.</p>" +
                    $"<p>You can now log in and start using the platform.</p>" +
                    $"<div style='text-align:center; margin:2rem 0;'>" +
                    $"<a href='https://localhost:7244/Identity/Account/Login' " +
                    $"style='background:#1a237e; color:#ffffff; padding:12px 28px; text-decoration:none; border-radius:6px; font-weight:bold; display:inline-block;'>" +
                    $"Log In to Vox Angelos</a>" +
                    $"</div>" +
                    $"<p style='color:#888; font-size:0.85rem;'>If you did not register for this account, please ignore this email.</p>" +
                    $"<p style='color:#888; font-size:0.85rem;'>— The Vox Angelos Team</p>" +
                    $"</div>");

                _logger.LogInformation("Admin approved citizen {UserId}", userId);
            }
            return RedirectToPage("/Admin/UserApplications");
        }

        public async Task<IActionResult> OnPostRejectAsync(string userId, string? rejectionReason)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.ApprovalStatus != "Rejected")
            {
                user.ApprovalStatus = "Rejected";
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    TempData["AdminError"] = "Could not reject this application — it may have just been changed by another admin.";
                    return RedirectToPage("/Admin/UserApplications");
                }

                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Your Vox Angelos Account Application",
                    $"<div style='font-family:Arial,sans-serif; max-width:480px; margin:0 auto;'>" +
                    $"<h2 style='color:#c62828;'>Application Update</h2>" +
                    $"<p>Hello,</p>" +
                    $"<p>We regret to inform you that your Vox Angelos citizen account has been <strong style='color:#c62828;'>rejected</strong>.</p>" +
                    $"<p><strong>Reason:</strong> {rejectionReason ?? "Does not meet verification requirements."}</p>" +
                    $"<p>If you believe this is an error, please contact support.</p>" +
                    $"<p style='color:#888; font-size:0.85rem;'>— The Vox Angelos Team</p>" +
                    $"</div>");

                _logger.LogInformation("Admin rejected citizen {UserId}", userId);
            }
            return RedirectToPage("/Admin/UserApplications");
        }
    }
}