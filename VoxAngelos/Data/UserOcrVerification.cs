using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class UserOcrVerification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int IdentityDocumentId { get; set; }

        public string? RawFullText { get; set; }
        public string? DetectedAddress { get; set; }
        public string? DetectedStreetAddress { get; set; }
        public string? DetectedLocality { get; set; }
        public string? DetectedProvince { get; set; }
        public string? DetectedBirthDate { get; set; }
        public string? DetectedCardExpirationDate { get; set; }
        public bool LocalityMatched { get; set; }

        // The actual accept/reject signal: whether "Angeles [City], Pampanga" was found
        // anywhere on the ID, independent of whether a specific barangay was also matched.
        public bool CityProvinceMatched { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? OcrConfidence { get; set; }

        public string? DetectionType { get; set; }
        public string? DetectedLanguageCode { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("IdentityDocumentId")]
        public UserIdentityDocument? IdentityDocument { get; set; }
    }
}