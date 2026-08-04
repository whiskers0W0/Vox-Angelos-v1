using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrAddressAndCardExpirationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "UserProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress",
                table: "UserProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CityProvinceMatched",
                table: "UserOcrVerifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DetectedCardExpirationDate",
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

            migrationBuilder.AddColumn<DateOnly>(
                name: "CardExpirationDate",
                table: "UserIdentityDocuments",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Province",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "StreetAddress",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "CityProvinceMatched",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedCardExpirationDate",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedProvince",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "DetectedStreetAddress",
                table: "UserOcrVerifications");

            migrationBuilder.DropColumn(
                name: "CardExpirationDate",
                table: "UserIdentityDocuments");
        }
    }
}
