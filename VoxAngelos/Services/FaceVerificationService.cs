using System.Net.Http.Headers;

namespace VoxAngelos.Services
{
    public class FaceVerificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FaceVerificationService> _logger;
        private readonly string _baseUrl;
        private const double DistanceThreshold = 0.55;

        public FaceVerificationService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<FaceVerificationService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _baseUrl = config["FaceApi:BaseUrl"]!;
        }

        public async Task<(bool isMatch, decimal confidence)> VerifyFacesAsync(
            string idPhotoPath,
            string selfiePhotoPath)
        {
            try
            {
                _logger.LogWarning("=== VerifyFacesAsync START ===");
                _logger.LogWarning("BaseUrl: {BaseUrl}", _baseUrl);
                _logger.LogWarning("ID photo path: {Path} | Exists: {Exists}",
                    idPhotoPath, File.Exists(idPhotoPath));
                _logger.LogWarning("Selfie path: {Path} | Exists: {Exists}",
                    selfiePhotoPath, File.Exists(selfiePhotoPath));

                if (!File.Exists(idPhotoPath) || !File.Exists(selfiePhotoPath))
                {
                    _logger.LogError("One or both image files do not exist on disk.");
                    return (false, 0);
                }

                using var form = new MultipartFormDataContent();

                var idPhotoBytes = await File.ReadAllBytesAsync(idPhotoPath);
                _logger.LogWarning("ID photo bytes read: {Count}", idPhotoBytes.Length);
                var idPhotoContent = new ByteArrayContent(idPhotoBytes);
                idPhotoContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    Path.GetExtension(idPhotoPath).ToLower() == ".png" ? "image/png" : "image/jpeg");
                form.Add(idPhotoContent, "idPhoto", Path.GetFileName(idPhotoPath));

                var selfieBytes = await File.ReadAllBytesAsync(selfiePhotoPath);
                _logger.LogWarning("Selfie bytes read: {Count}", selfieBytes.Length);
                var selfieContent = new ByteArrayContent(selfieBytes);
                selfieContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    Path.GetExtension(selfiePhotoPath).ToLower() == ".png" ? "image/png" : "image/jpeg");
                form.Add(selfieContent, "selfie", Path.GetFileName(selfiePhotoPath));

                _logger.LogWarning("Sending request to {Url}", $"{_baseUrl}/verify");
                var response = await _httpClient.PostAsync($"{_baseUrl}/verify", form);
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Flask response status: {Status}", response.StatusCode);
                _logger.LogWarning("Flask response body: {Json}", json);

                var result = System.Text.Json.JsonSerializer.Deserialize<FaceVerifyResult>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (result == null)
                {
                    _logger.LogError("Null response from Face API.");
                    return (false, 0);
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    _logger.LogWarning("Flask returned error: {Error}", result.Error);
                    return (false, 0);
                }

                double distance = result.Distance;

                // Correct confidence: 1.0 = perfect match, 0.0 = completely different
                decimal confidence = (decimal)Math.Round(Math.Max(0.0, 1.0 - distance), 4);

                // Use our own strict threshold instead of trusting Flask's 0.6
                bool isMatch = distance <= DistanceThreshold;

                _logger.LogWarning(
                    "Distance: {Distance} | Confidence: {Confidence} | DistanceThreshold: {Threshold} | IsMatch: {IsMatch}",
                    distance, confidence, DistanceThreshold, isMatch);

                return (isMatch, confidence);
            }
            catch (Exception ex)
            {
                _logger.LogError("=== VerifyFacesAsync EXCEPTION ===");
                _logger.LogError("Message: {Message}", ex.Message);
                _logger.LogError("StackTrace: {Stack}", ex.StackTrace);
                return (false, 0);
            }
        }

        private class FaceVerifyResult
        {
            public bool IsMatch { get; set; }
            public double Confidence { get; set; }
            public double Distance { get; set; }
            public string? Error { get; set; }
        }
    }
}