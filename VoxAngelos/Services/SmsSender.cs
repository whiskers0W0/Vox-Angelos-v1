using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VoxAngelos.Services
{
    public interface ISmsSender
    {
        Task SendSmsAsync(string phoneNumber, string message);
    }

    public class SmsSender : ISmsSender
    {
        private const string PhilSmsEndpoint = "https://dashboard.philsms.com/api/v3/sms/send";

        private readonly string? _apiToken;
        private readonly string? _senderId;
        private readonly IHostEnvironment _env;
        private readonly ILogger<SmsSender> _logger;
        private readonly HttpClient _httpClient;

        public SmsSender(IConfiguration configuration, IHostEnvironment env, ILogger<SmsSender> logger, IHttpClientFactory httpClientFactory)
        {
            _apiToken = configuration["PhilSms:ApiToken"];
            _senderId = configuration["PhilSms:SenderId"];
            _env = env;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient(nameof(SmsSender));

            if ((string.IsNullOrEmpty(_apiToken) || string.IsNullOrEmpty(_senderId)) && !_env.IsDevelopment())
                throw new InvalidOperationException("PhilSms:ApiToken and PhilSms:SenderId must both be configured.");
        }

        public async Task SendSmsAsync(string phoneNumber, string message)
        {
            if (_env.IsDevelopment())
            {
                // Print OTPs to the server console as a convenience backup (in case
                // PhilSMS delivery is delayed) — the real send below still runs.
                _logger.LogWarning(
                    "\n==================== DEV SMS ====================\nTo: {Phone}\n--------------------------------------------------\n{Body}\n===================================================",
                    phoneNumber, message);
            }

            if (string.IsNullOrEmpty(_apiToken) || string.IsNullOrEmpty(_senderId))
            {
                _logger.LogWarning(
                    "DEV SMS (PhilSms:ApiToken/SenderId not configured, not sent) To: {Phone}\n{Body}",
                    phoneNumber, message);
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, PhilSmsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new
            {
                recipient = ToPhilSmsFormat(phoneNumber),
                sender_id = _senderId,
                type = "plain",
                message
            });

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("PhilSMS to {Phone} failed: {StatusCode} {Body}", phoneNumber, response.StatusCode, body);
            }
        }

        // Numbers are stored app-wide as "+63XXXXXXXXXX" (see Register.cshtml.cs's
        // NormalizePhone), but PhilSMS's API expects "63XXXXXXXXXX" with no "+".
        private static string ToPhilSmsFormat(string phoneNumber) =>
            phoneNumber.TrimStart('+');
    }
}
