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
        public string? DetectedFirstName { get; set; }
        public string? DetectedMiddleName { get; set; }
        public string? DetectedLastName { get; set; }

        // The whole address block as one string — different ID types (National ID,
        // Passport, Voter's ID, Driver's License) format addresses too inconsistently
        // to reliably split into street/barangay/municipality/province.
        public string? DetectedAddress { get; set; }

        public string? DetectedBirthDate { get; set; }
        public string? DetectedCardExpirationDate { get; set; }

        // The actual accept/reject signal: whether "Angeles [City], Pampanga" was found
        // anywhere in the detected address.
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