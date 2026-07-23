using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

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
            if (_env.IsDevelopment())
            {
                // Print OTPs/reset links to the server console as a convenience backup
                // (in case Brevo delivery is delayed) — the real send below still runs.
                _logger.LogWarning(
                    "\n==================== DEV EMAIL ====================\nTo: {Email}\nSubject: {Subject}\n----------------------------------------------------\n{Body}\n=====================================================",
                    email, subject, StripHtml(htmlMessage));
            }

            if (string.IsNullOrEmpty(_apiKey))
            {
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

        // Turns "<a href='URL'>text</a>" into "text: URL" before stripping the remaining
        // tags, so the OTP/reset link is still clickable-readable in the console.
        private static string StripHtml(string html)
        {
            var withLinks = Regex.Replace(
                html,
                "<a[^>]+href=['\"]([^'\"]+)['\"][^>]*>(.*?)</a>",
                "$2: $1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var withLineBreaks = Regex.Replace(
                withLinks,
                "</(p|h[1-6]|div|li)>|<br\\s*/?>",
                "\n",
                RegexOptions.IgnoreCase);

            var noTags = Regex.Replace(withLineBreaks, "<[^>]+>", "");
            return WebUtility.HtmlDecode(noTags).Trim();
        }
    }
}
