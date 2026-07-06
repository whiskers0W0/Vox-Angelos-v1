using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace VoxAngelos.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly string? _apiKey;
        private readonly IHostEnvironment _env;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, IHostEnvironment env, ILogger<EmailSender> logger)
        {
            _apiKey = configuration["SendGrid:ApiKey"];
            _env = env;
            _logger = logger;

            if (string.IsNullOrEmpty(_apiKey) && !_env.IsDevelopment())
                throw new InvalidOperationException("SendGrid:ApiKey is not configured.");
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                // Development-only fallback when no SendGrid key is configured locally —
                // logs the email instead of sending it, so OTP codes are still readable.
                _logger.LogWarning(
                    "DEV EMAIL (SendGrid:ApiKey not configured, not sent) To: {Email} Subject: {Subject}\n{Body}",
                    email, subject, htmlMessage);
                return;
            }

            var client = new SendGridClient(_apiKey);
            var from = new EmailAddress("adrndgaming@gmail.com", "Vox Angelos");
            var to = new EmailAddress(email);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: htmlMessage);
            await client.SendEmailAsync(msg);
        }
    }
}
