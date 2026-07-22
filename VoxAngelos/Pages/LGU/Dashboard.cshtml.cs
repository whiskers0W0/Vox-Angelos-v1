using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public string DepartmentName { get; set; } = string.Empty;
        public int TotalConcerns { get; set; }
        public int TotalUnresolved { get; set; }
        public int TotalChosen { get; set; }
        public int TotalInProgress { get; set; }
        public int TotalResolved { get; set; }
        public int PendingRecommendations { get; set; }
        public string CategoryLabels { get; set; } = "[]";
        public string CategoryData { get; set; } = "[]";
        public string TrendLabels { get; set; } = "[]";
        public string TrendData { get; set; } = "[]";
        public double ResolutionRate { get; set; }
        public double AvgResponseDays { get; set; }
        public int TodayConcerns { get; set; }
        public List<BarangayCount> TopBarangays { get; set; } = new();

        public class BarangayCount
        {
            public string Barangay { get; set; } = string.Empty;
            public int Count { get; set; }
        }
        public List<RecentActivityItem> RecentActivities { get; set; } = new();
        public class RecentActivityItem
        {
            public string Type { get; set; } = "";      // "new", "update", "resolved"
            public string Text { get; set; } = "";
            public string TimeAgo { get; set; } = "";
        }
        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            DepartmentName = user?.Department ?? "LGU";

            var concerns = _db.Concerns
                .Where(c => c.Status != "Draft");

            TotalConcerns = await concerns.CountAsync();
            TotalUnresolved = await concerns.CountAsync(c => c.Status == "Unresolved");
            TotalChosen = await concerns.CountAsync(c => c.Status == "Chosen");
            TotalInProgress = await concerns.CountAsync(c => c.Status == "In Progress");
            TotalResolved = await concerns.CountAsync(c => c.Status == "Resolved");
            PendingRecommendations = await _db.Recommendations.CountAsync(r => r.Status == "Pending");

            // Category Breakdown
            var categoryGroups = await concerns
                .GroupBy(c => c.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            CategoryLabels = System.Text.Json.JsonSerializer.Serialize(
                categoryGroups.Select(g => g.Category ?? "Uncategorized").ToList());
            CategoryData = System.Text.Json.JsonSerializer.Serialize(
                categoryGroups.Select(g => g.Count).ToList());

            // Resolution Rate
            ResolutionRate = TotalConcerns > 0
                ? Math.Round((double)TotalResolved / TotalConcerns * 100, 1)
                : 0;

            // Average Response Time (days from submission to resolution)
            var resolvedConcerns = await concerns
                .Where(c => c.Status == "Resolved" && c.UpdatedAt != null)
                .ToListAsync();

            AvgResponseDays = resolvedConcerns.Any()
                ? Math.Round(resolvedConcerns.Average(c => (c.UpdatedAt!.Value - c.SubmittedAt).TotalDays), 1)
                : 0;

            

            // Top Barangays
            var barangayGroups = await concerns
                .Where(c => c.LocationName != null)
                .GroupBy(c => c.LocationName)
                .Select(g => new { Barangay = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(5)
                .ToListAsync();

            TopBarangays = barangayGroups.Select(g => new BarangayCount
            {
                Barangay = g.Barangay ?? "Unknown",
                Count = g.Count
            }).ToList();

            // Concern Trends — last 7 days
            var today = DateTime.UtcNow.Date;
            var labels = new List<string>();
            var data = new List<int>();

            // Today's Concerns — add this right here
            TodayConcerns = await concerns.CountAsync(c => c.SubmittedAt.Date == today);

            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                labels.Add(day.ToString("ddd"));
                var count = await concerns.CountAsync(c => c.SubmittedAt.Date == day);
                data.Add(count);
            }

            TrendLabels = System.Text.Json.JsonSerializer.Serialize(labels);
            TrendData = System.Text.Json.JsonSerializer.Serialize(data);

            // Recent Activity — last 10 concern events
            var recent = await concerns
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .OrderByDescending(c => c.UpdatedAt ?? c.SubmittedAt)
                .Take(10)
                .ToListAsync();

            foreach (var c in recent)
            {
                var location = c.LocationName ?? "Unknown location";
                var when = c.UpdatedAt ?? c.SubmittedAt;
                var diff = DateTime.UtcNow - when;
                var timeAgo = diff.TotalMinutes < 1 ? "just now"
                            : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes} mins ago"
                            : diff.TotalHours < 24 ? $"{(int)diff.TotalHours} hrs ago"
                            : $"{(int)diff.TotalDays} days ago";

                if (c.Status == "Unresolved" && c.UpdatedAt == null)
                {
                    RecentActivities.Add(new RecentActivityItem
                    {
                        Type = "assignment",
                        Text = $"New Concern: {c.Description.Split('.')[0]} at {location}",
                        TimeAgo = timeAgo
                    });
                }
                else if (c.Status == "Resolved")
                {
                    RecentActivities.Add(new RecentActivityItem
                    {
                        Type = "resolved",
                        Text = $"Resolved: {c.Description.Split('.')[0]} at {location}",
                        TimeAgo = timeAgo
                    });
                }
                else
                {
                    RecentActivities.Add(new RecentActivityItem
                    {
                        Type = "update",
                        Text = $"Status Update: {c.Description.Split('.')[0]} moved to {c.Status}",
                        TimeAgo = timeAgo
                    });
                }
            }
        }
    }
}
