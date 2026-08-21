using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;

namespace VoxAngelos.Services;

public sealed class AwsFaceVerificationService
{
    private readonly IAmazonRekognition _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AwsFaceVerificationService> _logger;

    public AwsFaceVerificationService(
        IConfiguration configuration,
        ILogger<AwsFaceVerificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        var region = RegionEndpoint.GetBySystemName(configuration["AWS:Region"] ?? "ap-northeast-1");
        var accessKey = configuration["AWS:AccessKey"];
        var secretKey = configuration["AWS:SecretKey"];
        _client = string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonRekognitionClient(region)
            : new AmazonRekognitionClient(new BasicAWSCredentials(accessKey, secretKey), region);
    }

    public string Region => _configuration["AWS:Region"] ?? "ap-northeast-1";
    public string IdentityPoolId => _configuration["AWS:IdentityPoolId"] ?? string.Empty;
    public float LivenessThreshold => _configuration.GetValue("AWS:LivenessThreshold", 90f);
    public float SimilarityThreshold => _configuration.GetValue("AWS:SimilarityThreshold", 95f);

    public async Task<string> CreateLivenessSessionAsync(CancellationToken cancellationToken) =>
        (await _client.CreateFaceLivenessSessionAsync(new CreateFaceLivenessSessionRequest
        {
            Settings = new CreateFaceLivenessSessionRequestSettings { AuditImagesLimit = 0 }
        }, cancellationToken)).SessionId;

    public async Task<AwsFaceResult> VerifyAsync(byte[] idImage, string sessionId, CancellationToken cancellationToken)
    {
        var liveness = await _client.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = sessionId }, cancellationToken);

        var livenessConfidence = liveness.Confidence ?? 0f;
        if (liveness.Status != LivenessSessionStatus.SUCCEEDED || livenessConfidence < LivenessThreshold)
            return AwsFaceResult.Failed("Liveness check did not pass.", livenessConfidence);

        var referenceBytes = liveness.ReferenceImage?.Bytes?.ToArray();
        if (referenceBytes is not { Length: > 0 })
            return AwsFaceResult.Failed("AWS did not return a usable reference image.", livenessConfidence);

        var comparison = await _client.CompareFacesAsync(new CompareFacesRequest
        {
            SourceImage = new Image { Bytes = new MemoryStream(idImage) },
            TargetImage = new Image { Bytes = new MemoryStream(referenceBytes) },
            // Ask AWS to return the candidate and apply our calibrated decision threshold
            // below. Passing the decision threshold here hides the actual similarity for
            // rejected comparisons, making safe threshold calibration impossible.
            SimilarityThreshold = 0,
            QualityFilter = QualityFilter.AUTO
        }, cancellationToken);
        var similarity = comparison.FaceMatches.Count == 0
            ? 0f
            : comparison.FaceMatches.Max(match => match.Similarity ?? 0f);

        _logger.LogInformation(
            "AWS registration face check completed. Liveness: {Liveness:F1}, Similarity: {Similarity:F1}, RequiredSimilarity: {Threshold:F1}",
            livenessConfidence, similarity, SimilarityThreshold);

        return similarity >= SimilarityThreshold
            ? new AwsFaceResult(true, livenessConfidence, similarity, referenceBytes, null)
            : AwsFaceResult.Failed("The live face did not match the ID portrait.", livenessConfidence, similarity);
    }
}

public sealed record AwsFaceResult(
    bool IsMatch,
    float LivenessConfidence,
    float Similarity,
    byte[]? ReferenceImage,
    string? Error)
{
    public static AwsFaceResult Failed(string error, float liveness = 0, float similarity = 0) =>
        new(false, liveness, similarity, null, error);
}
