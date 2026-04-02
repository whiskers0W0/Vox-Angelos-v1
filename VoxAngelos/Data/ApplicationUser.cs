using Microsoft.AspNetCore.Identity;

namespace VoxAngelos.Data
{
    public class ApplicationUser : IdentityUser
    {
        // Account timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // "Pending", "Approved", "Rejected"
        public string ApprovalStatus { get; set; } = "Pending";

        // Used by LGU and Admin accounts
        public string? EmployeeId { get; set; }

        // "Health Office", "Engineering Office", "Social Welfare", 
        // "Public Safety", "Agriculture"
        public string? Department { get; set; }

        // Navigation properties
        public UserProfile? UserProfile { get; set; }
        public AccountApproval? AccountApproval { get; set; }
        public ICollection<UserIdentityDocument> IdentityDocuments { get; set; } = new List<UserIdentityDocument>();
        public ICollection<UserFaceVerification> FaceVerifications { get; set; } = new List<UserFaceVerification>();
        public ICollection<UserOcrVerification> OcrVerifications { get; set; } = new List<UserOcrVerification>();
        public ICollection<UserLoginAudit> LoginAudits { get; set; } = new List<UserLoginAudit>();
    }
}