using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrNameAndMunicipalityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedFirstName",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedLastName",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedMiddleName",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedMunicipality",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedFirstName",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedLastName",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedMiddleName",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedMunicipality",
                table: "UserOcrVerifications");
        }
    }
}
