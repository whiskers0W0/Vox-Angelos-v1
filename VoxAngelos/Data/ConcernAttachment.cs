using System.ComponentModel.DataAnnotations;

namespace VoxAngelos.Data
{
    public class ConcernAttachment
    {
        public int Id { get; set; }

        public int ConcernId { get; set; }
        public Concern? Concern { get; set; }

        [Required]
        public string FilePath { get; set; } = string.Empty;

        // "image" or "video"
        public string FileType { get; set; } = "image";

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
