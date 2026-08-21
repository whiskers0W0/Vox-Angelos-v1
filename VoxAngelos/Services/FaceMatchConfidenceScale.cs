namespace VoxAngelos.Services;

public static class FaceMatchConfidenceScale
{
    /// <summary>
    /// Converts both legacy 0-1 confidence values and AWS 0-100 similarity
    /// values to the application's canonical 0-1 scale.
    /// </summary>
    public static decimal Normalize(decimal? value)
    {
        var confidence = value.GetValueOrDefault();
        if (confidence > 1m)
            confidence /= 100m;

        return Math.Clamp(confidence, 0m, 1m);
    }
}
