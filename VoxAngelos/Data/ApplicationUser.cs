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

        // "SWDO", "CEO", "CENRO", "ACDO", "PTRO", "OSCA", "PWDAO"
        public string? Department { get; set; }

        // Full office name shown alongside the abbreviation, e.g. "Social Welfare and Development Office"
        public string? DepartmentFullName { get; set; }

        // Admin-curated keywords used to steer NLP concern routing toward this LGU's department
        public List<string> Tags { get; set; } = new();

        // Optional citizen activity emails. Security and account-verification
        // emails are always sent independently of this preference.
        public bool EmailNotificationsEnabled { get; set; } = false;

        // Navigation properties
        public UserProfile? UserProfile { get; set; }
        public AccountApproval? AccountApproval { get; set; }
        public ICollection<UserIdentityDocument> IdentityDocuments { get; set; } = new List<UserIdentityDocument>();
        public ICollection<UserFaceVerification> FaceVerifications { get; set; } = new List<UserFaceVerification>();
        public ICollection<UserOcrVerification> OcrVerifications { get; set; } = new List<UserOcrVerification>();
        public ICollection<UserLoginAudit> LoginAudits { get; set; } = new List<UserLoginAudit>();
    }
}
