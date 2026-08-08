using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        public string TrendRange { get; set; } = "weekly";
        public string TrendRangeLabel { get; set; } = "7 days";
        public string TrendFrom { get; set; } = string.Empty;
        public string TrendTo { get; set; } = string.Empty;
        public string CategoryRange { get; set; } = "weekly";
        public string CategoryRangeLabel { get; set; } = "7 days";
        public string CategoryFrom { get; set; } = string.Empty;
        public string CategoryTo { get; set; } = string.Empty;
        public string BarangayRange { get; set; } = "weekly";
        public string BarangayRangeLabel { get; set; } = "7 days";
        public string BarangayFrom { get; set; } = string.Empty;
        public string BarangayTo { get; set; } = string.Empty;
        public int BarangayPeriodTotal { get; set; }
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
        public async Task OnGetAsync(
            string? trendRange, DateTime? trendFrom, DateTime? trendTo,
            string? categoryRange, DateTime? categoryFrom, DateTime? categoryTo,
            string? barangayRange, DateTime? barangayFrom, DateTime? barangayTo)
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

            var today = DateTime.UtcNow.Date;
            var categoryPeriod = ResolvePeriod(categoryRange, categoryFrom, categoryTo, today);
            CategoryRange = categoryPeriod.Range;
            CategoryRangeLabel = categoryPeriod.Label;
            CategoryFrom = categoryPeriod.Start.ToString("yyyy-MM-dd");
            CategoryTo = categoryPeriod.End.ToString("yyyy-MM-dd");

            var barangayPeriod = ResolvePeriod(barangayRange, barangayFrom, barangayTo, today);
            BarangayRange = barangayPeriod.Range;
            BarangayRangeLabel = barangayPeriod.Label;
            BarangayFrom = barangayPeriod.Start.ToString("yyyy-MM-dd");
            BarangayTo = barangayPeriod.End.ToString("yyyy-MM-dd");

            // Category Breakdown for the selected category period.
            var categoryGroups = await concerns
                .Where(c => c.SubmittedAt >= categoryPeriod.Start && c.SubmittedAt < categoryPeriod.End.AddDays(1))
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

            

            // Top Barangays — assign each mapped concern to the barangay boundary
            // that contains its saved latitude and longitude.
            var mappedConcernLocations = await concerns
                .Where(c => c.SubmittedAt >= barangayPeriod.Start && c.SubmittedAt < barangayPeriod.End.AddDays(1) &&
                    c.Latitude.HasValue && c.Longitude.HasValue)
                .Select(c => new { Latitude = c.Latitude!.Value, Longitude = c.Longitude!.Value })
                .ToListAsync();
            BarangayPeriodTotal = mappedConcernLocations.Count;

            var barangayBoundaries = await LoadBarangayBoundariesAsync();
            TopBarangays = mappedConcernLocations
                .Select(c => FindBarangay(c.Latitude, c.Longitude, barangayBoundaries))
                .Where(barangay => !string.IsNullOrWhiteSpace(barangay))
                .GroupBy(barangay => barangay!)
                .Select(group => new BarangayCount
                {
                    Barangay = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Barangay)
                .Take(5)
                .ToList();

            // Concern Trends — last 7 days
            var labels = new List<string>();
            var data = new List<int>();

            // Today's Concerns — add this right here
            TodayConcerns = await concerns.CountAsync(c => c.SubmittedAt.Date == today);

            TrendRange = trendRange?.ToLowerInvariant() switch
            {
                "monthly" => "monthly",
                "yearly" => "yearly",
                "custom" => "custom",
                _ => "weekly"
            };

            var startDate = TrendRange switch
            {
                "monthly" => today.AddDays(-29),
                "yearly" => new DateTime(today.Year, today.Month, 1).AddMonths(-11),
                "custom" when trendFrom.HasValue => trendFrom.Value.Date,
                _ => today.AddDays(-6)
            };
            var endDate = TrendRange == "custom" && trendTo.HasValue ? trendTo.Value.Date : today;

            if (startDate > endDate)
                (startDate, endDate) = (endDate, startDate);
            if (endDate > today)
                endDate = today;
            if (startDate < today.AddYears(-5))
                startDate = today.AddYears(-5);

            // PostgreSQL stores SubmittedAt as timestamp with time zone. Values from
            // new DateTime(...) and HTML date inputs have Kind=Unspecified, so make
            // the reporting boundaries explicitly UTC before using them as parameters.
            startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

            TrendFrom = startDate.ToString("yyyy-MM-dd");
            TrendTo = endDate.ToString("yyyy-MM-dd");
            TrendRangeLabel = TrendRange switch
            {
                "monthly" => "30 days",
                "yearly" => "12 months",
                "custom" => $"{startDate:MMM d, yyyy} – {endDate:MMM d, yyyy}",
                _ => "7 days"
            };

            var trendConcerns = await concerns
                .Where(c => c.SubmittedAt >= startDate && c.SubmittedAt < endDate.AddDays(1))
                .Select(c => c.SubmittedAt)
                .ToListAsync();

            var groupByMonth = TrendRange == "yearly" || (endDate - startDate).TotalDays > 60;
            if (groupByMonth)
            {
                var month = new DateTime(startDate.Year, startDate.Month, 1);
                var finalMonth = new DateTime(endDate.Year, endDate.Month, 1);
                while (month <= finalMonth)
                {
                    labels.Add(month.ToString("MMM yyyy"));
                    data.Add(trendConcerns.Count(date => date.Year == month.Year && date.Month == month.Month));
                    month = month.AddMonths(1);
                }
            }
            else
            {
                for (var day = startDate; day <= endDate; day = day.AddDays(1))
                {
                    labels.Add(TrendRange == "weekly" ? day.ToString("ddd") : day.ToString("MMM d"));
                    data.Add(trendConcerns.Count(date => date.Date == day));
                }
            }

            TrendLabels = System.Text.Json.JsonSerializer.Serialize(labels);
            TrendData = System.Text.Json.JsonSerializer.Serialize(data);

            // Recent Activity — latest 4 concern events
            var recent = await concerns
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .OrderByDescending(c => c.UpdatedAt ?? c.SubmittedAt)
                .Take(4)
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

        private static (string Range, DateTime Start, DateTime End, string Label) ResolvePeriod(
            string? requestedRange, DateTime? requestedFrom, DateTime? requestedTo, DateTime today)
        {
            var range = requestedRange?.ToLowerInvariant() switch
            {
                "monthly" => "monthly",
                "yearly" => "yearly",
                "custom" => "custom",
                _ => "weekly"
            };

            var start = range switch
            {
                "monthly" => today.AddDays(-29),
                "yearly" => new DateTime(today.Year, today.Month, 1).AddMonths(-11),
                "custom" when requestedFrom.HasValue => requestedFrom.Value.Date,
                _ => today.AddDays(-6)
            };
            var end = range == "custom" && requestedTo.HasValue ? requestedTo.Value.Date : today;

            if (start > end)
                (start, end) = (end, start);
            if (end > today)
                end = today;
            if (start < today.AddYears(-5))
                start = today.AddYears(-5);

            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            end = DateTime.SpecifyKind(end, DateTimeKind.Utc);
            var label = range switch
            {
                "monthly" => "30 days",
                "yearly" => "12 months",
                "custom" => $"{start:MMM d, yyyy} – {end:MMM d, yyyy}",
                _ => "7 days"
            };

            return (range, start, end, label);
        }

        private sealed class BarangayBoundary
        {
            public string Name { get; init; } = string.Empty;
            public string GeometryType { get; init; } = string.Empty;
            public JsonElement Coordinates { get; init; }
        }

        private static async Task<List<BarangayBoundary>> LoadBarangayBoundariesAsync()
        {
            var geoJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "geojson", "angeles-city-barangays.geojson");
            if (!System.IO.File.Exists(geoJsonPath))
            {
                return new List<BarangayBoundary>();
            }

            await using var stream = System.IO.File.OpenRead(geoJsonPath);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.GetProperty("features")
                .EnumerateArray()
                .Select(feature => new BarangayBoundary
                {
                    Name = feature.GetProperty("properties").GetProperty("name").GetString() ?? string.Empty,
                    GeometryType = feature.GetProperty("geometry").GetProperty("type").GetString() ?? string.Empty,
                    Coordinates = feature.GetProperty("geometry").GetProperty("coordinates").Clone()
                })
                .ToList();
        }

        private static string? FindBarangay(double latitude, double longitude, IEnumerable<BarangayBoundary> boundaries)
        {
            foreach (var boundary in boundaries)
            {
                var containsPoint = boundary.GeometryType switch
                {
                    "Polygon" => IsPointInPolygon(longitude, latitude, boundary.Coordinates),
                    "MultiPolygon" => boundary.Coordinates.EnumerateArray()
                        .Any(polygon => IsPointInPolygon(longitude, latitude, polygon)),
                    _ => false
                };

                if (containsPoint)
                {
                    return boundary.Name;
                }
            }

            return null;
        }

        private static bool IsPointInPolygon(double longitude, double latitude, JsonElement polygon)
        {
            if (polygon.GetArrayLength() == 0 || !IsPointInRing(longitude, latitude, polygon[0]))
            {
                return false;
            }

            return !polygon.EnumerateArray().Skip(1)
                .Any(hole => IsPointInRing(longitude, latitude, hole));
        }

        private static bool IsPointInRing(double longitude, double latitude, JsonElement ring)
        {
            var inside = false;
            var pointCount = ring.GetArrayLength();
            if (pointCount < 3)
            {
                return false;
            }

            for (int current = 0, previous = pointCount - 1; current < pointCount; previous = current++)
            {
                var currentLongitude = ring[current][0].GetDouble();
                var currentLatitude = ring[current][1].GetDouble();
                var previousLongitude = ring[previous][0].GetDouble();
                var previousLatitude = ring[previous][1].GetDouble();

                var crossesLatitude = (currentLatitude > latitude) != (previousLatitude > latitude);
                if (crossesLatitude && longitude < (previousLongitude - currentLongitude) * (latitude - currentLatitude) /
                    (previousLatitude - currentLatitude) + currentLongitude)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
