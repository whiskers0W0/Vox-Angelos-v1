using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAnonymousToRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnonymous",
                table: "Recommendations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAnonymous",
                table: "Recommendations");
        }
    }
}
