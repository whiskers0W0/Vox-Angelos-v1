using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserOcrVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeVerified",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "BarangayMatched",
                table: "UserOcrVerifications");

            migrationBuilder.RenameColumn(
                name: "DetectedCity",
                table: "UserOcrVerifications",
                newName: "DetectionType");

            migrationBuilder.RenameColumn(
                name: "DetectedBarangay",
                table: "UserOcrVerifications",
                newName: "DetectedRegion");

            migrationBuilder.RenameColumn(
                name: "CityMatched",
                table: "UserOcrVerifications",
                newName: "LocalityMatched");

            migrationBuilder.AlterColumn<string>(
                name: "DetectedLanguageCode",
                table: "UserOcrVerifications",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedAddress",
                table: "UserOcrVerifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedLocality",
                table: "UserOcrVerifications",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedAddress",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedLocality",
                table: "UserOcrVerifications");

            migrationBuilder.RenameColumn(
                name: "LocalityMatched",
                table: "UserOcrVerifications",
                newName: "CityMatched");

            migrationBuilder.RenameColumn(
                name: "DetectionType",
                table: "UserOcrVerifications",
                newName: "DetectedCity");

            migrationBuilder.RenameColumn(
                name: "DetectedRegion",
                table: "UserOcrVerifications",
                newName: "DetectedBarangay");

            migrationBuilder.AlterColumn<string>(
                name: "DetectedLanguageCode",
                table: "UserOcrVerifications",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AgeVerified",
                table: "UserOcrVerifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarangayMatched",
                table: "UserOcrVerifications",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
