using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class FixUserVerificationCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserFaceVerifications_AspNetUsers_UserId",
                table: "UserFaceVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_UserOcrVerifications_AspNetUsers_UserId",
                table: "UserOcrVerifications");

            migrationBuilder.AddForeignKey(
                name: "FK_UserFaceVerifications_AspNetUsers_UserId",
                table: "UserFaceVerifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserOcrVerifications_AspNetUsers_UserId",
                table: "UserOcrVerifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserFaceVerifications_AspNetUsers_UserId",
                table: "UserFaceVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_UserOcrVerifications_AspNetUsers_UserId",
                table: "UserOcrVerifications");

            migrationBuilder.AddForeignKey(
                name: "FK_UserFaceVerifications_AspNetUsers_UserId",
                table: "UserFaceVerifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserOcrVerifications_AspNetUsers_UserId",
                table: "UserOcrVerifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
