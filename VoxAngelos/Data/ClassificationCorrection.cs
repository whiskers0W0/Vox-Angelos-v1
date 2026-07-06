namespace VoxAngelos.Data
{
    // Logs each LGU verdict on a concern's NLP-assigned category (correct or reassigned)
    public class ClassificationCorrection
    {
        public int Id { get; set; }

        public int ConcernId { get; set; }
        public Concern Concern { get; set; } = null!;

        public string? PreviousCategory { get; set; }
        public string CorrectedCategory { get; set; } = string.Empty;
        public bool WasCorrect { get; set; }

        public string ReviewedByUserId { get; set; } = string.Empty;
        public ApplicationUser ReviewedBy { get; set; } = null!;

        public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    }
}
