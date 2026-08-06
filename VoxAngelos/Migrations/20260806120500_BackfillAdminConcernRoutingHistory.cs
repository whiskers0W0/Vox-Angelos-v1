using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxAngelos.Data;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260806120500_BackfillAdminConcernRoutingHistory")]
    public partial class BackfillAdminConcernRoutingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Concern routing already had a durable Admin timeline event before the
            // dedicated routing-history table existed. Recover those assignments so
            // the dashboard does not start at zero after this feature is deployed.
            migrationBuilder.Sql(
                """
                INSERT INTO "AdminRoutingAssignments"
                    ("SubmissionType", "SubmissionId", "Department", "AssignedByUserId", "AssignedAt")
                SELECT DISTINCT ON (concern."Id")
                    'Concern',
                    concern."Id",
                    concern."AssignedOffice",
                    NULL,
                    timeline."CreatedAt"
                FROM "ConcernTimelineEvents" AS timeline
                INNER JOIN "Concerns" AS concern ON concern."Id" = timeline."ConcernId"
                WHERE timeline."ActorRole" = 'Admin'
                  AND timeline."EventType" = 'Routed'
                  AND concern."AssignedOffice" IS NOT NULL
                  AND concern."AssignedOffice" <> ''
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "AdminRoutingAssignments" AS existing
                      WHERE existing."SubmissionType" = 'Concern'
                        AND existing."SubmissionId" = concern."Id")
                ORDER BY concern."Id", timeline."CreatedAt" ASC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Historical records cannot be distinguished safely from assignments made
            // after this migration, so rollback intentionally leaves the audit data.
        }
    }
}
