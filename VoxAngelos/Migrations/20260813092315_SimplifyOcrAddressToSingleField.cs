using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyOcrAddressToSingleField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedLocality",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedMunicipality",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedProvince",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedStreetAddress",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "LocalityMatched",
                table: "UserOcrVerifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedLocality",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedMunicipality",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedProvince",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedStreetAddress",
                table: "UserOcrVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LocalityMatched",
                table: "UserOcrVerifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
