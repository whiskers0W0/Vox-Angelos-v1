using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class AccountApproval
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        // "Pending", "Approved", "Rejected"
        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        // The Admin who reviewed (null until reviewed)
        public string? ReviewedByAdminId { get; set; }

        public string? RejectionReason { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }
}