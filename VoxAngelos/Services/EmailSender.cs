using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace VoxAngelos.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly string _smtpUser;
        private readonly string? _smtpPassword;
        private readonly IHostEnvironment _env;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, IHostEnvironment env, ILogger<EmailSender> logger)
        {
            _smtpUser = configuration["Smtp:Username"] ?? "adrndgaming@gmail.com";
            _smtpPassword = configuration["Smtp:Password"];
            _env = env;
            _logger = logger;

            if (string.IsNullOrEmpty(_smtpPassword) && !_env.IsDevelopment())
                throw new InvalidOperationException("Smtp:Password is not configured.");
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_smtpPassword))
            {
                // Development-only fallback when no SMTP app password is configured locally —
                // logs the email instead of sending it, so OTP codes are still readable.
                _logger.LogWarning(
                    "DEV EMAIL (Smtp:Password not configured, not sent) To: {Email} Subject: {Subject}\n{Body}",
                    email, subject, htmlMessage);
                return;
            }

            using var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(_smtpUser, _smtpPassword),
                EnableSsl = true
            };

            using var message = new MailMessage
            {
                From = new MailAddress(_smtpUser, "Vox Angelos"),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            message.To.Add(email);

            try
            {
                await client.SendMailAsync(message);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP email to {Email} failed: {Message}", email, ex.Message);
            }
        }
    }
}
