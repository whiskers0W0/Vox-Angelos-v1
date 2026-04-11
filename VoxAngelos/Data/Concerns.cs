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

        // "Unresolved", "In Progress", "Resolved"
        public string Status { get; set; } = "Unresolved";

        public string? LocationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // LGU fills this when updating status
        public string? LguNotes { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ConcernAttachment> Attachments { get; set; } = new List<ConcernAttachment>();
    }
}
