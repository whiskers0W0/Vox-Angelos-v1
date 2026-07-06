using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceVotesWithMultiCategoryRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationVotes");

            migrationBuilder.DropColumn(
                name: "Downvotes",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Upvotes",
                table: "Recommendations");

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "AvgFeasibility",
                table: "Recommendations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AvgRelevance",
                table: "Recommendations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AvgUrgency",
                table: "Recommendations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CompositeScore",
                table: "Recommendations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "RecommendationRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<int>(type: "integer", nullable: false),
                    CitizenId = table.Column<string>(type: "text", nullable: false),
                    UrgencyStars = table.Column<int>(type: "integer", nullable: false),
                    RelevanceStars = table.Column<int>(type: "integer", nullable: false),
                    FeasibilityStars = table.Column<int>(type: "integer", nullable: false),
                    RatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationRatings_AspNetUsers_CitizenId",
                        column: x => x.CitizenId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecommendationRatings_Recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_CompositeScore",
                table: "Recommendations",
                column: "CompositeScore");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRatings_CitizenId",
                table: "RecommendationRatings",
                column: "CitizenId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRatings_RecommendationId_CitizenId",
                table: "RecommendationRatings",
                columns: new[] { "RecommendationId", "CitizenId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationRatings");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_CompositeScore",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "AvgFeasibility",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "AvgRelevance",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "AvgUrgency",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "CompositeScore",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "Recommendations");

            migrationBuilder.AddColumn<int>(
                name: "Upvotes",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Downvotes",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RecommendationVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<int>(type: "integer", nullable: false),
                    CitizenId = table.Column<string>(type: "text", nullable: false),
                    VoteType = table.Column<string>(type: "text", nullable: false),
                    VotedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationVotes_Recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationVotes_RecommendationId_CitizenId",
                table: "RecommendationVotes",
                columns: new[] { "RecommendationId", "CitizenId" },
                unique: true);
        }
    }
}
