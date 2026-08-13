using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxAngelos.Services
{
    public class OcrResult
    {
        public bool Success { get; set; }
        public string? RawFullText { get; set; }
        public string? DetectedFirstName { get; set; }
        public string? DetectedMiddleName { get; set; }
        public string? DetectedLastName { get; set; }
        public string? DetectedBirthDate { get; set; }
        public string? DetectedCardExpiration { get; set; }
        public string? DetectedAddress { get; set; }

        // The pass/fail signal for "is this ID from Angeles City, Pampanga" — checked
        // against the literal city/province text in the address, not split into
        // street/barangay/municipality since different ID types format addresses
        // too inconsistently to reliably dissect.
        public bool CityProvinceMatched { get; set; }

        public decimal OcrConfidence { get; set; }
        public string? DetectionType { get; set; }
        public string? DetectedLanguageCode { get; set; }
        public string? Error { get; set; }
    }

    // Extracts ID fields using Gemini's vision API instead of the old pytesseract sidecar
    // (which broke in production: Windows-only tesseract path, and never returned half the
    // fields the UI needed). Gemini reads the fields directly out of the photo; the
    // city/province match itself stays a deterministic text check below rather than an LLM
    // judgment call, since a generative model can produce a plausible-looking answer on a
    // blurry image instead of failing closed the way literal OCR does.
    public class GeminiOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiOcrService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        private const string Prompt = """
            You are an OCR extraction system for Philippine government IDs — National ID,
            Passport, Voter's ID, Driver's License, and similar. Different ID types print
            the same information under different bilingual labels. Recognize all of these
            as the same field, and apply the same reasoning to any similar label you
            encounter that isn't listed here — match by meaning, not exact wording:
            - detectedFirstName: "First Name" / "Given Name(s)" / "Mga Pangalan"
            - detectedMiddleName: "Middle Name" / "Gitnang Apelyido" / "Gitnang Pangalan"
            - detectedLastName: "Last Name" / "Surname" / "Apelyido"
            - detectedBirthDate: "Birth Date" / "Birthday" / "Date of Birth" /
              "Petsa ng Kapanganakan"
            - detectedCardExpiration: "Expiration Date" / "Valid Until" / "Date of Expiry" /
              "Petsa ng Pagkawalang Bisa"
            - detectedAddress: "Address" / "Tirahan" / "Permanent Address"

            Extract fields from this ID photo and return ONLY the requested JSON fields.
            Rules:
            - Dates must be ISO format YYYY-MM-DD. If a date is not clearly legible, use an
              empty string.
            - If any field is not visible or not present on the ID, use an empty string —
              never guess or infer a plausible value.
            - Philippine IDs often print the surname first — still map it to
              detectedLastName, not detectedFirstName.
            - detectedAddress is the entire address block exactly as printed, as one single
              string. Do not split it into street/barangay/city/province.
            - rawFullText is all visible printed text on the card, space-separated.
            """;

        public GeminiOcrService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<GeminiOcrService> logger)
        {
            _httpClient = httpClientFactory.CreateClient(nameof(GeminiOcrService));
            _logger = logger;
            _apiKey = config["Gemini:ApiKey"]!;
            _model = config["Gemini:Model"] ?? "gemini-flash-latest";
        }

        public async Task<OcrResult> ExtractIdDataAsync(string idPhotoPath)
        {
            try
            {
                if (!File.Exists(idPhotoPath))
                    return new OcrResult { Success = false, Error = "File not found." };

                var bytes = await File.ReadAllBytesAsync(idPhotoPath);
                var base64 = Convert.ToBase64String(bytes);
                var mimeType = Path.GetExtension(idPhotoPath).ToLower() == ".png" ? "image/png" : "image/jpeg";

                var requestBody = new GeminiRequest
                {
                    Contents = new[]
                    {
                        new GeminiContent
                        {
                            Parts = new object[]
                            {
                                new { text = Prompt },
                                new { inline_data = new { mime_type = mimeType, data = base64 } }
                            }
                        }
                    },
                    GenerationConfig = new GeminiGenerationConfig
                    {
                        ResponseMimeType = "application/json",
                        ResponseSchema = ExtractionSchema
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini OCR returned {StatusCode}: {Body}", response.StatusCode, json);
                    return new OcrResult { Success = false, Error = $"Gemini returned {response.StatusCode}." };
                }

                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var extractedJson = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                if (string.IsNullOrWhiteSpace(extractedJson))
                {
                    _logger.LogWarning("Gemini OCR returned no extractable text. Raw response: {Json}", json);
                    return new OcrResult { Success = false, Error = "Gemini returned no content." };
                }

                var extracted = JsonSerializer.Deserialize<GeminiExtraction>(extractedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (extracted == null)
                    return new OcrResult { Success = false, Error = "Could not parse Gemini's extraction JSON." };

                return BuildResult(extracted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini OCR error");
                return new OcrResult { Success = false, Error = ex.Message };
            }
        }

        private static OcrResult BuildResult(GeminiExtraction extracted)
        {
            var address = extracted.DetectedAddress ?? "";

            // Only inspect the detected address, not the whole card — searching the raw
            // text risks a false match when a citizen's surname happens to be "Angeles"
            // (a common Filipino surname).
            var cityProvinceMatched = ContainsWord(address, "ANGELES") && ContainsWord(address, "PAMPANGA");

            var hasCoreFields = !string.IsNullOrWhiteSpace(extracted.DetectedBirthDate)
                || !string.IsNullOrWhiteSpace(address);

            return new OcrResult
            {
                Success = true,
                RawFullText = extracted.RawFullText,
                DetectedFirstName = string.IsNullOrWhiteSpace(extracted.DetectedFirstName) ? null : extracted.DetectedFirstName,
                DetectedMiddleName = string.IsNullOrWhiteSpace(extracted.DetectedMiddleName) ? null : extracted.DetectedMiddleName,
                DetectedLastName = string.IsNullOrWhiteSpace(extracted.DetectedLastName) ? null : extracted.DetectedLastName,
                DetectedBirthDate = string.IsNullOrWhiteSpace(extracted.DetectedBirthDate) ? null : extracted.DetectedBirthDate,
                DetectedCardExpiration = string.IsNullOrWhiteSpace(extracted.DetectedCardExpiration) ? null : extracted.DetectedCardExpiration,
                DetectedAddress = string.IsNullOrWhiteSpace(address) ? null : address,
                CityProvinceMatched = cityProvinceMatched,
                OcrConfidence = hasCoreFields ? 0.95m : 0m,
                DetectionType = "gemini",
                DetectedLanguageCode = "en"
            };
        }

        private static bool ContainsWord(string haystack, string word)
        {
            if (string.IsNullOrWhiteSpace(haystack)) return false;
            var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b";
            return System.Text.RegularExpressions.Regex.IsMatch(haystack, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static readonly object ExtractionSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                rawFullText = new { type = "STRING" },
                detectedFirstName = new { type = "STRING" },
                detectedMiddleName = new { type = "STRING" },
                detectedLastName = new { type = "STRING" },
                detectedBirthDate = new { type = "STRING" },
                detectedCardExpiration = new { type = "STRING" },
                detectedAddress = new { type = "STRING" }
            },
            required = new[]
            {
                "rawFullText", "detectedFirstName", "detectedMiddleName", "detectedLastName",
                "detectedBirthDate", "detectedCardExpiration", "detectedAddress"
            }
        };

        // ── Gemini REST API request/response shapes ────────────────────────

        private class GeminiRequest
        {
            [JsonPropertyName("contents")]
            public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

            [JsonPropertyName("generationConfig")]
            public GeminiGenerationConfig GenerationConfig { get; set; } = new();
        }

        private class GeminiContent
        {
            [JsonPropertyName("parts")]
            public object[] Parts { get; set; } = Array.Empty<object>();
        }

        private class GeminiGenerationConfig
        {
            [JsonPropertyName("responseMimeType")]
            public string ResponseMimeType { get; set; } = "application/json";

            [JsonPropertyName("responseSchema")]
            public object ResponseSchema { get; set; } = new();
        }

        private class GeminiResponse
        {
            public GeminiCandidate[]? Candidates { get; set; }
        }

        private class GeminiCandidate
        {
            public GeminiResponseContent? Content { get; set; }
        }

        private class GeminiResponseContent
        {
            public GeminiResponsePart[]? Parts { get; set; }
        }

        private class GeminiResponsePart
        {
            public string? Text { get; set; }
        }

        private class GeminiExtraction
        {
            public string? RawFullText { get; set; }
            public string? DetectedFirstName { get; set; }
            public string? DetectedMiddleName { get; set; }
            public string? DetectedLastName { get; set; }
            public string? DetectedBirthDate { get; set; }
            public string? DetectedCardExpiration { get; set; }
            public string? DetectedAddress { get; set; }
        }
    }
}
