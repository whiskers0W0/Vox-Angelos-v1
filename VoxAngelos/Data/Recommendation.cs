namespace VoxAngelos.Data
{
    public class Recommendation
    {
        public int Id { get; set; }
        public string CitizenId { get; set; } = string.Empty;
        public ApplicationUser Citizen { get; set; } = null!;

        public string Justification { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Beneficiaries { get; set; } = string.Empty;
        public int EstimatedPeopleAffected { get; set; }
        public ICollection<RecommendationAttachment> Attachments { get; set; } = new List<RecommendationAttachment>();
        public string Status { get; set; } = "Pending";
        public string? LguNotes { get; set; }
        public string? ReviewedByLguId { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public int Upvotes { get; set; } = 0;
        public int Downvotes { get; set; } = 0;

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        public ICollection<RecommendationVote> Votes { get; set; } = new List<RecommendationVote>();
    }

    public class RecommendationVote
    {
        public int Id { get; set; }
        public int RecommendationId { get; set; }
        public Recommendation Recommendation { get; set; } = null!;
        public string CitizenId { get; set; } = string.Empty;
        public string VoteType { get; set; } = string.Empty;
        public DateTime VotedAt { get; set; } = DateTime.UtcNow;
    }

    public class RecommendationAttachment
    {
        public int Id { get; set; }
        public int RecommendationId { get; set; }
        public Recommendation Recommendation { get; set; } = null!;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty; // "image", "video", "document"
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}