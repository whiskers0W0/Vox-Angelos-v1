using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoriesToLguAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Categories",
                table: "AspNetUsers",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            // Backfill the category→department mapping already established by the training
            // taxonomy for the 7 departments that have a real office today. The 6 categories
            // with no real office yet (health, education, employment, animal_welfare,
            // disaster_risk_reduction, utilities) are intentionally left unassigned here.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['infrastructure'] WHERE "Department" = 'CEO';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['urban_planning', 'business_permits'] WHERE "Department" = 'ACDO';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['environment', 'sanitation', 'waste_management'] WHERE "Department" = 'CENRO';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['public_safety', 'traffic'] WHERE "Department" = 'PTRO';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['pwd_affairs'] WHERE "Department" = 'PWDAO';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['senior_citizen'] WHERE "Department" = 'OSCA';
                UPDATE "AspNetUsers" SET "Categories" = ARRAY['social_services'] WHERE "Department" = 'SWDO';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Categories",
                table: "AspNetUsers");
        }
    }
}
