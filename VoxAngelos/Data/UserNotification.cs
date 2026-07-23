using System.ComponentModel.DataAnnotations;

namespace VoxAngelos.Data
{
    public class UserNotification
    {
        public int Id { get; set; }

        [Required]
        public string RecipientUserId { get; set; } = string.Empty;
        public ApplicationUser? RecipientUser { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string NotificationType { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? SenderRole { get; set; }

        [MaxLength(200)]
        public string? SenderName { get; set; }

        [MaxLength(500)]
        public string? LinkUrl { get; set; }

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
    }
}
