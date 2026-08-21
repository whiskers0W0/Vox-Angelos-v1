using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxAngelos.Services
{
    public class GeminiClassificationResult
    {
        // Null means Gemini judged the text a genuine concern/recommendation that doesn't
        // clearly fit any department — routed to Uncategorized for human triage, same as a
        // FUTURE_EXPANSION category from the NLP model.
        public string? Department { get; set; }
        public double Confidence { get; set; }
    }

    // Second-opinion verifier that runs after the NLP (TF-IDF + SVM) model on every concern
    // and recommendation. The NLP prediction and the keyword-matching signal are both passed
    // in as advisory context — grounding Gemini's read of the text against the same
    // department vocabulary the keyword scorer uses — but Gemini's own verdict is what's
    // trusted and stored: it catches the cases where the NLP model's category or the keyword
    // overlap doesn't actually match what the citizen meant (mixed English/Tagalog/Kapampangan
    // phrasing, sarcasm, a concern that mentions one department's keywords while actually
    // describing another's problem, etc). Reuses the same Gemini:ApiKey/Gemini:Model
    // configuration and REST call shape as GeminiOcrService.
    public class GeminiConcernClassifierService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiConcernClassifierService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        public GeminiConcernClassifierService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<GeminiConcernClassifierService> logger)
        {
            _httpClient = httpClientFactory.CreateClient(nameof(GeminiConcernClassifierService));
            _logger = logger;
            _apiKey = config["Gemini:ApiKey"]!;
            _model = config["Gemini:Model"] ?? "gemini-flash-latest";
        }

        private const int MaxRetries = 2;
        private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

        /// <summary>
        /// Returns Gemini's final department verdict, or null if the call itself failed
        /// (rate limit, no billing credits, network issue, malformed response) — callers must
        /// treat a null return as "verifier unavailable" and fall back to the NLP/keyword
        /// chain rather than as "no department."
        /// </summary>
        public async Task<GeminiClassificationResult?> ClassifyAsync(
            string description,
            string? nlpDepartment,
            string? nlpCategory,
            IReadOnlyDictionary<string, int> keywordScores)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                return null;

            try
            {
                var prompt = BuildPrompt(description, nlpDepartment, nlpCategory, keywordScores);

                var requestBody = new GeminiRequest
                {
                    Contents = new[]
                    {
                        new GeminiContent { Parts = new object[] { new { text = prompt } } }
                    },
                    GenerationConfig = new GeminiGenerationConfig
                    {
                        ResponseMimeType = "application/json",
                        ResponseSchema = VerdictSchema
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                var requestJson = JsonSerializer.Serialize(requestBody);

                HttpResponseMessage response;
                string json;
                int attempt = 0;

                while (true)
                {
                    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(url, content);
                    json = await response.Content.ReadAsStringAsync();

                    // 402/403 covers a Gemini account with no prepay credit balance — treated
                    // the same as a rate limit: an availability/billing issue, not a signal
                    // about the text, so the caller falls back rather than mis-routing.
                    var isRetryable = response.StatusCode == HttpStatusCode.TooManyRequests
                        || response.StatusCode == HttpStatusCode.ServiceUnavailable;

                    if (response.IsSuccessStatusCode || !isRetryable || attempt >= MaxRetries)
                        break;

                    var delay = response.Headers.RetryAfter?.Delta ?? RetryDelays[attempt];
                    _logger.LogWarning(
                        "Gemini classifier returned {StatusCode} (attempt {Attempt}/{Max}) — retrying in {Delay}s",
                        response.StatusCode, attempt + 1, MaxRetries, delay.TotalSeconds);
                    await Task.Delay(delay);
                    attempt++;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini classifier returned {StatusCode}: {Body}", response.StatusCode, json);
                    return null;
                }

                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var verdictJson = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                if (string.IsNullOrWhiteSpace(verdictJson))
                {
                    _logger.LogWarning("Gemini classifier returned no extractable text. Raw response: {Json}", json);
                    return null;
                }

                var verdict = JsonSerializer.Deserialize<GeminiVerdict>(verdictJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (verdict == null)
                    return null;

                // Never trust a hallucinated department string — anything outside the known
                // list (or the NONE sentinel) is treated as a failed call, same as any other
                // malformed response, so the caller falls back to the NLP/keyword chain.
                if (verdict.Department == "NONE" || string.IsNullOrWhiteSpace(verdict.Department))
                    return new GeminiClassificationResult { Department = null, Confidence = verdict.Confidence };

                if (!ConcernClassificationService.Departments.Contains(verdict.Department))
                {
                    _logger.LogWarning(
                        "Gemini classifier returned an unrecognized department {Department} — discarding", verdict.Department);
                    return null;
                }

                return new GeminiClassificationResult { Department = verdict.Department, Confidence = verdict.Confidence };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini classifier call failed, falling back to NLP/keyword chain");
                return null;
            }
        }

        private static string BuildPrompt(
            string description, string? nlpDepartment, string? nlpCategory, IReadOnlyDictionary<string, int> keywordScores)
        {
            var sb = new StringBuilder();
            sb.AppendLine("""
                You are the final classification authority for a Local Government Unit (LGU) citizen
                concern/recommendation routing system in Angeles City, Pampanga, Philippines. Citizens
                write in English, Tagalog, Kapampangan, or a mix of the three.

                Decide which ONE department below should handle the submission, based on what the text
                actually describes — not just surface keyword overlap. The keyword lists are a guide to
                each department's scope, not a strict rule; a submission can mention a department's
                keyword while actually describing a different department's problem (or no real local
                concern at all), and vice versa.
                """);
            sb.AppendLine();
            sb.AppendLine("Departments and representative keywords:");
            foreach (var (department, keywords) in ConcernClassificationService.DefaultKeywordsByDepartment)
            {
                sb.AppendLine($"- {department}: {string.Join(", ", keywords)}");
            }
            sb.AppendLine();
            sb.AppendLine("LGU-specific routing rules that override generic keyword overlap — apply these");
            sb.AppendLine("even when the text doesn't literally contain the listed keywords:");
            foreach (var rule in RoutingRules)
                sb.AppendLine($"- {rule}");
            sb.AppendLine();
            sb.AppendLine("Two automated signals already ran on this text and are advisory input only —");
            sb.AppendLine("you are the final decision-maker and should override either when your own");
            sb.AppendLine("reading of the text disagrees:");
            sb.AppendLine($"- NLP model prediction: department={nlpDepartment ?? "none"}, category={nlpCategory ?? "none"}");
            sb.AppendLine(keywordScores.Count == 0
                ? "- Keyword-matching signal: no department scored above zero"
                : $"- Keyword-matching signal (raw hit counts, not normalized): {string.Join(", ", keywordScores.Select(kv => $"{kv.Key}={kv.Value}"))}");
            sb.AppendLine();
            sb.AppendLine("Submission text:");
            sb.AppendLine("\"\"\"");
            sb.AppendLine(description);
            sb.AppendLine("\"\"\"");
            sb.AppendLine();
            sb.AppendLine("""
                Respond with your final verdict as JSON. Set department to the department code, or to
                "NONE" if this is a genuine local concern/recommendation that doesn't clearly fit any
                department above. Only use "NONE" if you're truly unsure — prefer the closest reasonable
                department over leaving it unclassified. confidence is your own 0-1 estimate of how sure
                you are in this verdict.
                """);

            return sb.ToString();
        }

        // Domain corrections from LGU staff that a generic reading of the department keyword
        // lists wouldn't reliably produce on its own — add to this list as more misroutes like
        // this surface (e.g. an admin-facing corrections tool later), rather than only relying
        // on new keywords, since these are about which office actually owns the *situation*,
        // not just which words appear in the text.
        private static readonly string[] RoutingRules =
        [
            "Neighbor/community noise disturbances — loud videoke/karaoke, loud music, noisy " +
                "parties, especially late at night or past curfew — go to SWDO as a peace-and-order " +
                "/ community-welfare concern. Do NOT route these to CENRO (that department's \"noise " +
                "pollution\" scope means industrial/environmental noise, not a neighbor's karaoke " +
                "machine) or to PTRO (traffic enforcement has nothing to do with residential noise)."
        ];

        private static readonly object VerdictSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                department = new
                {
                    type = "STRING",
                    @enum = ConcernClassificationService.Departments.Append("NONE").ToArray()
                },
                confidence = new { type = "NUMBER" }
            },
            required = new[] { "department", "confidence" }
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

        private class GeminiVerdict
        {
            public string? Department { get; set; }
            public double Confidence { get; set; }
        }
    }
}
