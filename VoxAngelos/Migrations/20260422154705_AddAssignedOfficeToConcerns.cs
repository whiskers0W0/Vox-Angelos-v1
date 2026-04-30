using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedOfficeToConcerns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedOffice",
                table: "Concerns",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedOffice",
                table: "Concerns");
        }
    }
}
