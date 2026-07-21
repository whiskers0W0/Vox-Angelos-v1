namespace VoxAngelos.Data
{
    public class Recommendation
    {
        public int Id { get; set; }
        public string CitizenId { get; set; } = string.Empty;
        public ApplicationUser Citizen { get; set; } = null!;

        public string Justification { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        // Populated by NLP after submission — LGU department this should route to
        public string? AssignedOffice { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Beneficiaries { get; set; } = string.Empty;
        public int EstimatedPeopleAffected { get; set; }
        // Hides the citizen's identity from the public Discover feed only.
        // Authorized LGU staff can still identify the submitter for follow-up.
        public bool IsAnonymous { get; set; }
        public ICollection<RecommendationAttachment> Attachments { get; set; } = new List<RecommendationAttachment>();
        public string Status { get; set; } = "Pending";
        public string? LguNotes { get; set; }
        public string? ReviewedByLguId { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // Cached aggregates — recomputed atomically by RecommendationRatingService each time
        // a rating is submitted, so the leaderboard never has to aggregate raw rating rows
        // at read time.
        public int RatingCount { get; set; } = 0;
        public double AvgUrgency { get; set; } = 0;
        public double AvgRelevance { get; set; } = 0;
        public double AvgFeasibility { get; set; } = 0;
        public double CompositeScore { get; set; } = 0;

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        public ICollection<RecommendationRating> Ratings { get; set; } = new List<RecommendationRating>();
    }

    // One citizen's multi-category star rating for one recommendation (1-5 stars each).
    // Editable — a citizen can re-rate, which upserts this row rather than adding a new one.
    public class RecommendationRating
    {
        public int Id { get; set; }
        public int RecommendationId { get; set; }
        public Recommendation Recommendation { get; set; } = null!;
        public string CitizenId { get; set; } = string.Empty;
        public ApplicationUser Citizen { get; set; } = null!;

        public int UrgencyStars { get; set; }
        public int RelevanceStars { get; set; }
        public int FeasibilityStars { get; set; }

        public DateTime RatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
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
