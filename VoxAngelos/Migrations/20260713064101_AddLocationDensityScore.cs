using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationDensityScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "Location",
                table: "Concerns",
                type: "geography (Point, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationDensityScore",
                table: "Concerns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Concerns_Location",
                table: "Concerns",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "GIST");

            // Backfill Location from the existing plain Latitude/Longitude columns for
            // concerns submitted before this migration, then seed their density scores
            // from the now-populated geography column.
            migrationBuilder.Sql("""
                UPDATE "Concerns"
                SET "Location" = ST_SetSRID(ST_MakePoint("Longitude", "Latitude"), 4326)::geography
                WHERE "Latitude" IS NOT NULL AND "Longitude" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "Concerns" c
                SET "LocationDensityScore" = (
                    SELECT COUNT(*) FROM "Concerns" n
                    WHERE n."Id" != c."Id"
                      AND n."Location" IS NOT NULL
                      AND n."SubmittedAt" >= now() - interval '30 days'
                      AND n."Status" != 'Resolved'
                      AND ST_DWithin(c."Location", n."Location", 300)
                )
                WHERE c."Location" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Concerns_Location",
                table: "Concerns");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Concerns");

            migrationBuilder.DropColumn(
                name: "LocationDensityScore",
                table: "Concerns");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
