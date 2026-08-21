using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxAngelos.Data;

#nullable disable

namespace VoxAngelos.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260821103000_AddLivenessConfidenceToFaceVerification")]
    public class AddLivenessConfidenceToFaceVerification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "UserFaceVerifications"
                    ADD COLUMN IF NOT EXISTS "LivenessConfidence" numeric(5,4) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LivenessConfidence",
                table: "UserFaceVerifications");
        }
    }
}
