using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class NotificationsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public IActionResult OnGet()
        {
            return RedirectToPage("/LGU/Dashboard");
        }

        public async Task<IActionResult> OnPostMarkNotificationReadAsync(int notificationId)
        {
            var lguUser = await _userManager.GetUserAsync(User);
            if (lguUser == null)
            {
                return Unauthorized();
            }

            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(item =>
                    item.Id == notificationId &&
                    item.RecipientUserId == lguUser.Id);

            if (notification == null)
            {
                return NotFound();
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
        }
    }
}
