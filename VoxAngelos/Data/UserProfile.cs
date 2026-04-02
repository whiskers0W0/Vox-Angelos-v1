using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoxAngelos.Data
{
    public class UserProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }

        // For OCR age verification
        public DateOnly? BirthDate { get; set; }

        // For locality verification
        public string? Barangay { get; set; }
        public string? City { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }
}