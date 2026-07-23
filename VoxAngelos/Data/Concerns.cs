using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace VoxAngelos.Data
{
    public class Concern
    {
        public int Id { get; set; }

        [Required]
        public string CitizenId { get; set; } = string.Empty;
        public ApplicationUser? Citizen { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        // Populated by NLP after submission — starts null
        public string? Category { get; set; }

        public string? AssignedOffice { get; set; }

        // "Unresolved", "In Progress", "Resolved"
        public string Status { get; set; } = "Unresolved";

        public string? LocationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Geography point mirroring Latitude/Longitude, mapped to a PostGIS
        // geography(Point,4326) column with a GIST index (see ApplicationDbContext and
        // the AddLocationDensityScore migration) so density queries below run as an
        // index-backed ST_DWithin search instead of a per-row distance loop.
        public NetTopologySuite.Geometries.Point? Location { get; set; }

        // Count of other unresolved concerns within the density radius (see
        // UrgencyScoreService) submitted within the density time window. Recomputed for
        // this concern and its neighbors whenever a new concern is submitted nearby.
        public int LocationDensityScore { get; set; }

        // LGU fills this when updating status
        public string? LguNotes { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ConcernAttachment> Attachments { get; set; } = new List<ConcernAttachment>();
        public ICollection<ConcernTimelineEvent> TimelineEvents { get; set; } = new List<ConcernTimelineEvent>();
    }
}
