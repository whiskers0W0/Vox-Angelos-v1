using System.Text;
using System.Text.Json;

namespace VoxAngelos.Services
{
    public class HfClassificationResult
    {
        public string? Category { get; set; }
        public string? Office { get; set; }

        // 0..1 softmax-normalized margin from the SVM's decision_function — a proxy for how
        // sure the model is, not a calibrated probability. Null if the Space hasn't been
        // updated to return it yet, in which case callers should treat the prediction as
        // trusted (fail open) rather than reject it for a missing field.
        public double? Confidence { get; set; }
    }

    // Calls the TF-IDF + SVM concern classifier hosted alongside the OCR/face
    // verification endpoints on the same Hugging Face Space (FaceApi:BaseUrl).
    public class HfConcernClassifierService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HfConcernClassifierService> _logger;
        private readonly string _baseUrl;

        public HfConcernClassifierService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<HfConcernClassifierService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _baseUrl = config["FaceApi:BaseUrl"]!;
        }

        // Returns the raw model prediction (category + the HF Space's own office_map.json
        // guess), or null if the call failed. Deciding whether that's a usable department is
        // ConcernClassificationService's job — it checks the admin-editable category→department
        // mapping first, since that can override this response without redeploying the Space.
        public async Task<HfClassificationResult?> ClassifyAsync(string description)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { text = description });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/classify", content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "HF classifier returned {StatusCode}: {Body}", response.StatusCode, json);
                    return null;
                }

                return JsonSerializer.Deserialize<HfClassificationResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HF classifier call failed, falling back to existing classifier");
                return null;
            }
        }
    }
}
