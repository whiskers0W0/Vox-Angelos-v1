using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class ReviewApplicationModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ReviewApplicationModel> _logger;

        public ReviewApplicationModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<ReviewApplicationModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
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

        public async Task<IActionResult> OnPostApproveAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.ApprovalStatus = "Approved";
                await _userManager.UpdateAsync(user);

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
            if (user != null)
            {
                user.ApprovalStatus = "Rejected";
                await _userManager.UpdateAsync(user);

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