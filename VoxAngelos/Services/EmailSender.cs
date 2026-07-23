using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VoxAngelos.Services
{
    public class EmailSender : IEmailSender
    {
        private const string BrevoEndpoint = "https://api.brevo.com/v3/smtp/email";

        private readonly string? _apiKey;
        private readonly string _fromEmail;
        private readonly IHostEnvironment _env;
        private readonly ILogger<EmailSender> _logger;
        private readonly HttpClient _httpClient;

        public EmailSender(IConfiguration configuration, IHostEnvironment env, ILogger<EmailSender> logger, IHttpClientFactory httpClientFactory)
        {
            _apiKey = configuration["Brevo:ApiKey"];
            _fromEmail = configuration["Brevo:FromEmail"] ?? "adrndgaming@gmail.com";
            _env = env;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient(nameof(EmailSender));

            if (string.IsNullOrEmpty(_apiKey) && !_env.IsDevelopment())
                throw new InvalidOperationException("Brevo:ApiKey is not configured.");
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                // Development-only fallback when no Brevo API key is configured locally —
                // logs the email instead of sending it, so OTP codes and reset links are still readable.
                _logger.LogWarning(
                    "DEV EMAIL (Brevo:ApiKey not configured, not sent) To: {Email} Subject: {Subject}\n{Body}",
                    email, subject, htmlMessage);
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BrevoEndpoint);
            request.Headers.TryAddWithoutValidation("api-key", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new
            {
                sender = new { email = _fromEmail, name = "Vox Angelos" },
                to = new[] { new { email } },
                subject,
                htmlContent = htmlMessage
            });

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Brevo email to {Email} failed: {StatusCode} {Body}", email, response.StatusCode, body);
            }
        }
    }
}
