using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class UserFaceVerification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int IdentityDocumentId { get; set; }

        public string? LiveSelfiePath { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? MatchConfidence { get; set; }

        [MaxLength(50)]
        public string? VerificationStatus { get; set; }

        public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("IdentityDocumentId")]
        public UserIdentityDocument? IdentityDocument { get; set; }
    }
}