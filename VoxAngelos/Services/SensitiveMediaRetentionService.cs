using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
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
        private readonly Cloudinary _cloudinary;

        public SensitiveMediaRetentionService(
            IServiceProvider services,
            IConfiguration configuration,
            ILogger<SensitiveMediaRetentionService> logger,
            Cloudinary cloudinary)
        {
            _services = services;
            _configuration = configuration;
            _logger = logger;
            _cloudinary = cloudinary;
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
            await PurgeReviewedMediaAsync(userId: null, ct);
        }

        /// <summary>
        /// Immediately removes the protected ID and selfie media for one reviewed
        /// account. The scheduled sweep remains responsible for retrying any
        /// Cloudinary deletion that does not complete.
        /// </summary>
        public async Task<int> PurgeUserMediaAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("A user ID is required.", nameof(userId));

            return await PurgeReviewedMediaAsync(userId, ct);
        }

        private async Task<int> PurgeReviewedMediaAsync(string? userId, CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var purgedCount = 0;

            var reviewedIds = await db.UserIdentityDocuments
                .Where(d => (d.IdPhotoPath != null || d.IdPhotoCloudinaryPublicId != null) &&
                            d.User != null && d.User.ApprovalStatus != "Pending" &&
                            (userId == null || d.UserId == userId))
                .ToListAsync(ct);

            foreach (var doc in reviewedIds)
            {
                if (doc.IdPhotoPath != null)
                {
                    DeleteFileIfExists(IdentityDocumentStorage.IdsFolder(env), doc.IdPhotoPath);
                    doc.IdPhotoPath = null;
                    purgedCount++;
                }

                if (await DeletePrivateCloudinaryAssetAsync(doc.IdPhotoCloudinaryPublicId))
                {
                    doc.IdPhotoCloudinaryPublicId = null;
                    doc.IdPhotoCloudinaryFormat = null;
                    purgedCount++;
                }
            }

            var reviewedSelfies = await db.UserFaceVerifications
                .Where(f => (f.LiveSelfiePath != null || f.LiveSelfieCloudinaryPublicId != null) &&
                            f.User != null && f.User.ApprovalStatus != "Pending" &&
                            (userId == null || f.UserId == userId))
                .ToListAsync(ct);

            foreach (var selfie in reviewedSelfies)
            {
                if (selfie.LiveSelfiePath != null)
                {
                    DeleteFileIfExists(IdentityDocumentStorage.SelfiesFolder(env), selfie.LiveSelfiePath);
                    selfie.LiveSelfiePath = null;
                    purgedCount++;
                }

                if (await DeletePrivateCloudinaryAssetAsync(selfie.LiveSelfieCloudinaryPublicId))
                {
                    selfie.LiveSelfieCloudinaryPublicId = null;
                    selfie.LiveSelfieCloudinaryFormat = null;
                    purgedCount++;
                }
            }

            if (purgedCount > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Sensitive media retention sweep purged {Count} protected media copy/copies for reviewed accounts.",
                    purgedCount);
            }

            return purgedCount;
        }

        private async Task<bool> DeletePrivateCloudinaryAssetAsync(string? publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId)) return false;

            try
            {
                var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId)
                {
                    Type = "private",
                    ResourceType = ResourceType.Image
                });

                if (string.Equals(result.Result, "ok", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result.Result, "not found", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result.Result, "not_found", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _logger.LogWarning(
                    "Private Cloudinary media deletion did not complete for asset {PublicId}. Result: {Result}",
                    publicId,
                    result.Result);
            }
            catch (Exception ex)
            {
                // Leave the database reference intact. The next retention cycle will retry
                // the deletion instead of silently retaining an untracked sensitive asset.
                _logger.LogWarning(ex, "Private Cloudinary media deletion failed for asset {PublicId}.", publicId);
            }

            return false;
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
