using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class UserLoginAudit
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        // e.g. "EmailPassword", "OTP", "SecurityKey"
        [MaxLength(30)]
        public string? LoginMethod { get; set; }

        public bool Success { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }
}