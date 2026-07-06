namespace VoxAngelos.Data
{
    // Word -> department weight adjustments accumulated from LGU classification feedback,
    // layered on top of ConcernClassificationService's static DepartmentKeywords.
    public class LearnedKeyword
    {
        public int Id { get; set; }

        public string Word { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Weight { get; set; } = 0;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
