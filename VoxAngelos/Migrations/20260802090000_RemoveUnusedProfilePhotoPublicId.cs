using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxAngelos.Data;

#nullable disable

namespace VoxAngelos.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260802090000_RemoveUnusedProfilePhotoPublicId")]
    public partial class RemoveUnusedProfilePhotoPublicId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoPublicId",
                table: "AspNetUsers");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoPublicId",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }
    }
}
