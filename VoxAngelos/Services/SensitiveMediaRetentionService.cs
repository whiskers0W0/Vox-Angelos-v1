using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Services
{
    // Periodically purges the physical ID-photo and selfie files (and their DB path
    // references) once they are older than the configured retention window. The
    // verification outcome (status, confidence, OCR fields) is kept for audit purposes —
    // only the raw biometric/ID images themselves are deleted, per Data Privacy Act
    // (RA 10173) minimization requirements for sensitive personal information.
    public class SensitiveMediaRetentionService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SensitiveMediaRetentionService> _logger;

        public SensitiveMediaRetentionService(
            IServiceProvider services,
            IConfiguration configuration,
            ILogger<SensitiveMediaRetentionService> logger)
        {
            _services = services;
            _configuration = configuration;
            _logger = logger;
        }

        private int RetentionDays => _configuration.GetValue<int?>("MediaRetention:SensitiveDataRetentionDays") ?? 3;
        private int PollIntervalMinutes => _configuration.GetValue<int?>("MediaRetention:PollIntervalMinutes") ?? 60;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PurgeExpiredMediaAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sensitive media retention sweep failed.");
                }

                await Task.Delay(TimeSpan.FromMinutes(PollIntervalMinutes), stoppingToken);
            }
        }

        private async Task PurgeExpiredMediaAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var purgedCount = 0;

            var expiredIds = await db.UserIdentityDocuments
                .Where(d => d.IdPhotoPath != null && d.UploadedAt < cutoff)
                .ToListAsync(ct);

            foreach (var doc in expiredIds)
            {
                DeleteFileIfExists(env, "ids", doc.IdPhotoPath);
                doc.IdPhotoPath = null;
                purgedCount++;
            }

            var expiredSelfies = await db.UserFaceVerifications
                .Where(f => f.LiveSelfiePath != null && f.VerifiedAt < cutoff)
                .ToListAsync(ct);

            foreach (var selfie in expiredSelfies)
            {
                DeleteFileIfExists(env, "selfies", selfie.LiveSelfiePath);
                selfie.LiveSelfiePath = null;
                purgedCount++;
            }

            if (purgedCount > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Sensitive media retention sweep purged {Count} file(s) older than {Days} day(s).",
                    purgedCount, RetentionDays);
            }
        }

        private void DeleteFileIfExists(IWebHostEnvironment env, string subfolder, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            // Stored paths are bare filenames (see Register.cshtml.cs) — reject anything
            // that isn't, so a malformed value can never be used to escape the uploads folder.
            if (Path.GetFileName(fileName) != fileName) return;

            var fullPath = Path.Combine(env.WebRootPath, "uploads", subfolder, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }
}
