using System.Net.Http.Headers;

namespace VoxAngelos.Services
{
    public class IdValidationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IdValidationService> _logger;
        private readonly string _baseUrl;

        public IdValidationService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<IdValidationService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _baseUrl = config["FaceApi:BaseUrl"]!;
        }

        // side: "front" or "back" — only National ID ever sends "back". reasonCode is a
        // stable machine-readable code (see the reason codes in /validate-id on the HF
        // Space) that callers can map to a specific, non-generic user-facing message
        // instead of just relaying whatever free-text "reason" the Space returned.
        public async Task<(bool isValid, string reasonCode, string reason)> ValidateIdAsync(
            string idPhotoPath, string idType, string side = "front")
        {
            try
            {
                using var form = new MultipartFormDataContent();

                var idPhotoBytes = await File.ReadAllBytesAsync(idPhotoPath);
                var idPhotoContent = new ByteArrayContent(idPhotoBytes);
                idPhotoContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                form.Add(idPhotoContent, "idPhoto", Path.GetFileName(idPhotoPath));
                form.Add(new StringContent(idType), "idType");
                form.Add(new StringContent(side), "side");

                var response = await _httpClient.PostAsync($"{_baseUrl}/validate-id", form);
                var json = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("ID validation response: {Json}", json);

                var result = System.Text.Json.JsonSerializer.Deserialize<IdValidationResult>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (result == null)
                    return (false, "UNKNOWN", "Could not validate ID. Please try again.");

                return (result.IsValidId, result.ReasonCode ?? "UNKNOWN", result.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError("ID validation failed: {Message}", ex.Message);
                return (false, "SERVICE_UNAVAILABLE", "ID validation service unavailable. Please try again.");
            }
        }

        private class IdValidationResult
        {
            public bool IsValidId { get; set; }
            public string? ReasonCode { get; set; }
            public string Reason { get; set; } = string.Empty;
        }
    }
}
