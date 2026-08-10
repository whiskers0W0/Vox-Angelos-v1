using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.Admin;

[Authorize(Policy = "RequireAdminRole")]
public class NotificationsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public IActionResult OnGet() => RedirectToPage("/Admin/Index");

    public async Task<IActionResult> OnPostMarkNotificationReadAsync(int notificationId)
    {
        var admin = await _userManager.GetUserAsync(User);
        if (admin == null) return Unauthorized();

        var notification = await _db.UserNotifications.FirstOrDefaultAsync(item =>
            item.Id == notificationId && item.RecipientUserId == admin.Id);
        if (notification == null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostSetEmailNotificationsAsync(bool enabled)
    {
        var admin = await _userManager.GetUserAsync(User);
        if (admin == null) return Unauthorized();

        if (enabled && (!admin.EmailConfirmed || string.IsNullOrWhiteSpace(admin.Email)))
        {
            return BadRequest(new
            {
                success = false,
                message = "A confirmed Admin email is required before enabling email alerts."
            });
        }

        admin.EmailNotificationsEnabled = enabled;
        var result = await _userManager.UpdateAsync(admin);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "We could not update your email preference. Please try again."
            });
        }

        return new JsonResult(new
        {
            success = true,
            enabled,
            message = enabled
                ? $"Admin alerts will be sent to {admin.Email}."
                : "Admin email alerts have been turned off."
        });
    }
}
