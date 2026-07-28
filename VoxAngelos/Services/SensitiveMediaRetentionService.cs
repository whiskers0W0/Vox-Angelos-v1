using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Services
{
    // Periodically purges the physical ID-photo and selfie files (and their DB path
    // references) once an admin has reviewed the owning account (Approved or Rejected).
    // Purging is tied to review completion rather than a fixed time window because
    // account review can take longer than any fixed window, and purging on a timer
    // risked deleting the images an admin still needed mid-review. The verification
    // outcome (status, confidence, OCR fields) is kept for audit purposes — only the
    // raw biometric/ID images themselves are deleted, per Data Privacy Act (RA 10173)
    // minimization requirements for sensitive personal information.
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

            var purgedCount = 0;

            var reviewedIds = await db.UserIdentityDocuments
                .Where(d => d.IdPhotoPath != null && d.User != null && d.User.ApprovalStatus != "Pending")
                .ToListAsync(ct);

            foreach (var doc in reviewedIds)
            {
                DeleteFileIfExists(IdentityDocumentStorage.IdsFolder(env), doc.IdPhotoPath);
                doc.IdPhotoPath = null;
                purgedCount++;
            }

            var reviewedSelfies = await db.UserFaceVerifications
                .Where(f => f.LiveSelfiePath != null && f.User != null && f.User.ApprovalStatus != "Pending")
                .ToListAsync(ct);

            foreach (var selfie in reviewedSelfies)
            {
                DeleteFileIfExists(IdentityDocumentStorage.SelfiesFolder(env), selfie.LiveSelfiePath);
                selfie.LiveSelfiePath = null;
                purgedCount++;
            }

            if (purgedCount > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Sensitive media retention sweep purged {Count} file(s) for reviewed accounts.",
                    purgedCount);
            }
        }

        private void DeleteFileIfExists(string folder, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            // Stored paths are bare filenames (see Register.cshtml.cs) — reject anything
            // that isn't, so a malformed value can never be used to escape the identity-documents folder.
            if (Path.GetFileName(fileName) != fileName) return;

            var fullPath = Path.Combine(folder, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }
}
