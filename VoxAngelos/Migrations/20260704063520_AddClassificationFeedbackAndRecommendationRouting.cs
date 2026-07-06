using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class AddClassificationFeedbackAndRecommendationRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedOffice",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClassificationCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConcernId = table.Column<int>(type: "integer", nullable: false),
                    PreviousCategory = table.Column<string>(type: "text", nullable: true),
                    CorrectedCategory = table.Column<string>(type: "text", nullable: false),
                    WasCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "text", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationCorrections_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassificationCorrections_Concerns_ConcernId",
                        column: x => x.ConcernId,
                        principalTable: "Concerns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnedKeywords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Word = table.Column<string>(type: "text", nullable: false),
                    Department = table.Column<string>(type: "text", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedKeywords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCorrections_ConcernId",
                table: "ClassificationCorrections",
                column: "ConcernId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCorrections_ReviewedByUserId",
                table: "ClassificationCorrections",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnedKeywords_Word_Department",
                table: "LearnedKeywords",
                columns: new[] { "Word", "Department" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationCorrections");

            migrationBuilder.DropTable(
                name: "LearnedKeywords");

            migrationBuilder.DropColumn(
                name: "AssignedOffice",
                table: "Recommendations");
        }
    }
}
