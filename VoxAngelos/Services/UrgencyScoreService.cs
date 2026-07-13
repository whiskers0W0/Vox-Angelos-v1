using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Services
{
    // Computes the Location Density Score component of the Urgency Algorithm: how many
    // other active concerns were reported near a given pin recently. All distance work
    // is delegated to PostGIS (ST_DWithin against a geography(Point,4326) column backed
    // by a GIST index — see the AddLocationDensityScore migration) rather than pulling
    // rows into .NET and looping, so this stays index-backed and fast even once the
    // Concerns table holds thousands of pins.
    public class UrgencyScoreService
    {
        private readonly ApplicationDbContext _db;

        public UrgencyScoreService(ApplicationDbContext db)
        {
            _db = db;
        }

        public const int DensityRadiusMeters = 300;
        public const int DensityWindowDays = 30;

        // Sets the new concern's geography point from its Latitude/Longitude, then
        // recomputes its own density score and every existing neighbor's, so the map
        // and LGU dashboard reflect the new report immediately.
        public async Task ApplyLocationAsync(Concern concern)
        {
            if (concern.Latitude is null || concern.Longitude is null) return;

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Concerns"
                SET "Location" = ST_SetSRID(ST_MakePoint({concern.Longitude}, {concern.Latitude}), 4326)::geography
                WHERE "Id" = {concern.Id}
                """);

            var cutoff = DateTime.UtcNow.AddDays(-DensityWindowDays);

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Concerns" c
                SET "LocationDensityScore" = (
                    SELECT COUNT(*) FROM "Concerns" n
                    WHERE n."Id" != c."Id"
                      AND n."Location" IS NOT NULL
                      AND n."SubmittedAt" >= {cutoff}
                      AND n."Status" != 'Resolved'
                      AND ST_DWithin(c."Location", n."Location", {DensityRadiusMeters})
                )
                WHERE c."Id" = {concern.Id}
                """);

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Concerns" n
                SET "LocationDensityScore" = (
                    SELECT COUNT(*) FROM "Concerns" c2
                    WHERE c2."Id" != n."Id"
                      AND c2."Location" IS NOT NULL
                      AND c2."SubmittedAt" >= {cutoff}
                      AND c2."Status" != 'Resolved'
                      AND ST_DWithin(n."Location", c2."Location", {DensityRadiusMeters})
                )
                WHERE n."Id" != {concern.Id}
                  AND n."Location" IS NOT NULL
                  AND n."SubmittedAt" >= {cutoff}
                  AND ST_DWithin(
                        (SELECT "Location" FROM "Concerns" WHERE "Id" = {concern.Id}),
                        n."Location", {DensityRadiusMeters})
                """);
        }
    }
}
