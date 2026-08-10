using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddIdPhotoBackToIdentityDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdPhotoBackCloudinaryFormat",
                table: "UserIdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdPhotoBackCloudinaryPublicId",
                table: "UserIdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdPhotoBackPath",
                table: "UserIdentityDocuments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdPhotoBackCloudinaryFormat",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "IdPhotoBackCloudinaryPublicId",
                table: "UserIdentityDocuments");

            migrationBuilder.DropColumn(
                name: "IdPhotoBackPath",
                table: "UserIdentityDocuments");
        }
    }
}
