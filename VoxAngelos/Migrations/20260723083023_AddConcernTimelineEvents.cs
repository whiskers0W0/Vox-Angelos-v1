using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddConcernTimelineEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConcernTimelineEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConcernId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcernTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConcernTimelineEvents_Concerns_ConcernId",
                        column: x => x.ConcernId,
                        principalTable: "Concerns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConcernTimelineEvents_ConcernId_CreatedAt",
                table: "ConcernTimelineEvents",
                columns: new[] { "ConcernId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConcernTimelineEvents");
        }
    }
}
