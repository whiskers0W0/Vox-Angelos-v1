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

        public async Task<(bool isValid, string reason)> ValidateIdAsync(string idPhotoPath)
        {
            try
            {
                using var form = new MultipartFormDataContent();

                var idPhotoBytes = await File.ReadAllBytesAsync(idPhotoPath);
                var idPhotoContent = new ByteArrayContent(idPhotoBytes);
                idPhotoContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                form.Add(idPhotoContent, "idPhoto", Path.GetFileName(idPhotoPath));

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
                    return (false, "Could not validate ID. Please try again.");

                return (result.IsValidId, result.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError("ID validation failed: {Message}", ex.Message);
                return (false, "ID validation service unavailable. Please try again.");
            }
        }

        private class IdValidationResult
        {
            public bool IsValidId { get; set; }
            public string Reason { get; set; } = string.Empty;
        }
    }
}
