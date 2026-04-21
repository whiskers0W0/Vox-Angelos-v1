using Microsoft.AspNetCore.Authorization;
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

        public HeatmapModel(ApplicationDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        public string ConcernsJson { get; set; } = "[]";
        public string GoogleMapsApiKey => _configuration["GoogleMaps:ApiKey"] ?? "";

        public async Task OnGetAsync()
        {
            var concerns = await _db.Concerns
                .Include(c => c.Citizen)
                .ThenInclude(u => u.UserProfile)
                .Where(c => c.Latitude != null && c.Longitude != null)
                .Select(c => new
                {
                    id = c.Id,
                    lat = c.Latitude,
                    lng = c.Longitude,
                    description = c.Description,
                    status = c.Status,
                    category = c.Category ?? "Uncategorized",
                    location = c.LocationName ?? "No location provided",
                    submittedAt = c.SubmittedAt.ToString("MMM dd, yyyy"),
                    citizenName = c.Citizen.UserProfile != null
                        ? $"{c.Citizen.UserProfile.FirstName} {c.Citizen.UserProfile.LastName}"
                        : c.Citizen.Email
                })
                .ToListAsync();

            ConcernsJson = JsonSerializer.Serialize(concerns);
        }
    }
}