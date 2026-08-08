using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using System.Text.Json;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class HeatmapModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly UserManager<ApplicationUser> _userManager;

        public HeatmapModel(
            ApplicationDbContext db,
            IConfiguration configuration,
            UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _configuration = configuration;
            _userManager = userManager;
        }

        public string ConcernsJson { get; set; } = "[]";
        public string GoogleMapsApiKey => _configuration["GoogleMaps:ApiKey"] ?? "";
        public string CurrentUserDepartment { get; set; } = string.Empty;
        //public string BarangayGeoJson { get; set; } = "{}";


        public async Task OnGetAsync()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            CurrentUserDepartment = currentUser?.Department?.Trim() ?? string.Empty;

            var concerns = await _db.Concerns
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .Where(c => c.Status != "Draft" && c.Latitude != null && c.Longitude != null)
                .Select(c => new
                {
                    id = c.Id,
                    lat = c.Latitude,
                    lng = c.Longitude,
                    description = c.Description,
                    status = c.Status,
                    category = c.Category ?? "Uncategorized",
                    department = c.Category ?? string.Empty,
                    canOpen = !string.IsNullOrEmpty(CurrentUserDepartment) && c.Category == CurrentUserDepartment,
                    location = c.LocationName ?? "No location provided",
                    locationDensityScore = c.LocationDensityScore,
                    submittedAt = c.SubmittedAt.ToString("MMM dd, yyyy"),
                    citizenName = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName} {c.Citizen.UserProfile.LastName}"
                        : c.Citizen.Email
                })
                .ToListAsync();

            ConcernsJson = JsonSerializer.Serialize(concerns);

            //var geoJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "geojson", "angeles-city-barangays.geojson");
            //if (System.IO.File.Exists(geoJsonPath))
            //{
            //    BarangayGeoJson = await System.IO.File.ReadAllTextAsync(geoJsonPath);
            //}

        }
    }
}
