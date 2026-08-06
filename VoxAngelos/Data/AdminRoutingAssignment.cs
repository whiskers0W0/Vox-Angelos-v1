using System.ComponentModel.DataAnnotations;

namespace VoxAngelos.Data
{
    public class AdminRoutingAssignment
    {
        public int Id { get; set; }

        [Required, MaxLength(24)]
        public string SubmissionType { get; set; } = string.Empty;

        public int SubmissionId { get; set; }

        [Required, MaxLength(50)]
        public string Department { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? AssignedByUserId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}
