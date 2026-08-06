using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRoutingAssignmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminRoutingAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SubmissionId = table.Column<int>(type: "integer", nullable: false),
                    Department = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AssignedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRoutingAssignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoutingAssignments_Department_AssignedAt",
                table: "AdminRoutingAssignments",
                columns: new[] { "Department", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoutingAssignments_SubmissionType_SubmissionId",
                table: "AdminRoutingAssignments",
                columns: new[] { "SubmissionType", "SubmissionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminRoutingAssignments");
        }
    }
}
