using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationAttachmentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecommendationAttachment_Recommendations_RecommendationId",
                table: "RecommendationAttachment");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RecommendationAttachment",
                table: "RecommendationAttachment");

            migrationBuilder.RenameTable(
                name: "RecommendationAttachment",
                newName: "RecommendationAttachments");

            migrationBuilder.RenameIndex(
                name: "IX_RecommendationAttachment_RecommendationId",
                table: "RecommendationAttachments",
                newName: "IX_RecommendationAttachments_RecommendationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RecommendationAttachments",
                table: "RecommendationAttachments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecommendationAttachments_Recommendations_RecommendationId",
                table: "RecommendationAttachments",
                column: "RecommendationId",
                principalTable: "Recommendations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecommendationAttachments_Recommendations_RecommendationId",
                table: "RecommendationAttachments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RecommendationAttachments",
                table: "RecommendationAttachments");

            migrationBuilder.RenameTable(
                name: "RecommendationAttachments",
                newName: "RecommendationAttachment");

            migrationBuilder.RenameIndex(
                name: "IX_RecommendationAttachments_RecommendationId",
                table: "RecommendationAttachment",
                newName: "IX_RecommendationAttachment_RecommendationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RecommendationAttachment",
                table: "RecommendationAttachment",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecommendationAttachment_Recommendations_RecommendationId",
                table: "RecommendationAttachment",
                column: "RecommendationId",
                principalTable: "Recommendations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
