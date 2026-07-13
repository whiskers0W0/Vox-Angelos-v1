using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using VoxAngelos.Data;

namespace VoxAngelos.Hubs
{
    // Push-only hub: clients never call server methods on it, they just join a group
    // and wait for a broadcast. Kept intentionally thin — the payloads it relays are
    // built and sent by the Razor Page handlers that already own the corresponding
    // data (Create.cshtml.cs, User/Index.cshtml.cs, LGU/Index.cshtml.cs) via
    // IHubContext<FeedHub>, so there is exactly one place each event shape is defined.
    [Authorize]
    public class FeedHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public FeedHub(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // Every signed-in citizen watching the Discover feed joins this group.
        public const string DiscoverGroup = "discover-feed";

        public static string LguDepartmentGroup(string department) => $"lgu-{department}";

        public override async Task OnConnectedAsync()
        {
            if (Context.User?.IsInRole("User") == true)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, DiscoverGroup);
            }
            else if (Context.User?.IsInRole("LGU") == true)
            {
                var user = await _userManager.GetUserAsync(Context.User);
                if (!string.IsNullOrEmpty(user?.Department))
                    await Groups.AddToGroupAsync(Context.ConnectionId, LguDepartmentGroup(user.Department));
            }

            await base.OnConnectedAsync();
        }
    }
}
