using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class MakeClassificationCorrectionConcernIdUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassificationCorrections_ConcernId",
                table: "ClassificationCorrections");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCorrections_ConcernId",
                table: "ClassificationCorrections",
                column: "ConcernId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassificationCorrections_ConcernId",
                table: "ClassificationCorrections");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCorrections_ConcernId",
                table: "ClassificationCorrections",
                column: "ConcernId");
        }
    }
}
