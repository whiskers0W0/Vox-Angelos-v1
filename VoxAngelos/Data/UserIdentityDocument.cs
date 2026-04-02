using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class UserIdentityDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public string? IdType { get; set; }

        public string? IdPhotoPath { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public ICollection<UserFaceVerification> FaceVerifications { get; set; } = new List<UserFaceVerification>();

        public ICollection<UserOcrVerification> OcrVerifications { get; set; } = new List<UserOcrVerification>();
    }
}