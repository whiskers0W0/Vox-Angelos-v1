using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateIdentityMediaCloudBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudinaryLastUploadError",
                table: "UserIdentityDocuments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CloudinaryNextRetryAt",
                table: "UserIdentityDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloudinaryUploadAttempts",
                table: "UserIdentityDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdPhotoCloudinaryFormat",
                table: "UserIdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdPhotoCloudinaryPublicId",
                table: "UserIdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveSelfieCloudinaryFormat",
                table: "UserFaceVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveSelfieCloudinaryPublicId",
                table: "UserFaceVerifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudinaryLastUploadError",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "CloudinaryNextRetryAt",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "CloudinaryUploadAttempts",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "IdPhotoCloudinaryFormat",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "IdPhotoCloudinaryPublicId",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "LiveSelfieCloudinaryFormat",
                table: "UserFaceVerifications");

            migrationBuilder.DropColumn(
                name: "LiveSelfieCloudinaryPublicId",
                table: "UserFaceVerifications");
        }
    }
}
