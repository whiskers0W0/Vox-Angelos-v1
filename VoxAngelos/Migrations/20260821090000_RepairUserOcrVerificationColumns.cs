using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxAngelos.Data;

#nullable disable

namespace VoxAngelos.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260821090000_RepairUserOcrVerificationColumns")]
    public class RepairUserOcrVerificationColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "UserOcrVerifications"
                    ADD COLUMN IF NOT EXISTS "RawFullText" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedAddress" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedStreetAddress" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedLocality" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedProvince" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedBirthDate" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedCardExpirationDate" text NULL,
                    ADD COLUMN IF NOT EXISTS "LocalityMatched" boolean NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS "CityProvinceMatched" boolean NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS "OcrConfidence" numeric(5,4) NULL,
                    ADD COLUMN IF NOT EXISTS "DetectionType" text NULL,
                    ADD COLUMN IF NOT EXISTS "DetectedLanguageCode" text NULL,
                    ADD COLUMN IF NOT EXISTS "ProcessedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This repairs schema drift in databases that may already contain any
            // subset of these columns. Removing them would risk deleting existing data.
        }
    }
}
