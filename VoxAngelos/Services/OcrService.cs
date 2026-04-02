using System.Net.Http.Headers;
using System.Text.Json;

namespace VoxAngelos.Services
{
    public class OcrResult
    {
        public bool Success { get; set; }
        public string? RawFullText { get; set; }
        public string? DetectedBirthDate { get; set; }
        public string? DetectedAddress { get; set; }
        public string? DetectedLocality { get; set; }
        public string? DetectedRegion { get; set; }
        public bool LocalityMatched { get; set; }
        public decimal OcrConfidence { get; set; }
        public string? DetectionType { get; set; }
        public string? DetectedLanguageCode { get; set; }
        public string? Error { get; set; }
    }

    public class OcrService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OcrService> _logger;
        private readonly string _baseUrl;

        public OcrService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<OcrService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _baseUrl = config["FaceApi:BaseUrl"]!;
        }

        public async Task<OcrResult> ExtractIdDataAsync(string idPhotoPath)
        {
            try
            {
                if (!File.Exists(idPhotoPath))
                    return new OcrResult { Success = false, Error = "File not found." };

                using var form = new MultipartFormDataContent();
                var bytes = await File.ReadAllBytesAsync(idPhotoPath);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    Path.GetExtension(idPhotoPath).ToLower() == ".png" ? "image/png" : "image/jpeg");
                form.Add(content, "idPhoto", Path.GetFileName(idPhotoPath));

                var response = await _httpClient.PostAsync($"{_baseUrl}/ocr-id", form);
                var json = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("OCR response: {Json}", json);

                var result = JsonSerializer.Deserialize<OcrResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return result ?? new OcrResult { Success = false, Error = "Null response." };
            }
            catch (Exception ex)
            {
                _logger.LogError("OCR error: {Message}", ex.Message);
                return new OcrResult { Success = false, Error = ex.Message };
            }
        }
    }
}