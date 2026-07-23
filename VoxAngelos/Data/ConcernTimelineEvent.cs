using System.ComponentModel.DataAnnotations;

namespace VoxAngelos.Data
{
    public class ConcernTimelineEvent
    {
        public int Id { get; set; }

        [Required]
        public int ConcernId { get; set; }
        public Concern Concern { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Status { get; set; } = string.Empty;

        public string? Message { get; set; }

        [Required]
        [MaxLength(50)]
        public string ActorRole { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? ActorName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
