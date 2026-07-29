using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Services;

/// <summary>
/// Backs up pending applicants' protected local ID and selfie images to private
/// Cloudinary storage. It never processes applications that are already reviewed.
/// </summary>
public sealed class IdentityMediaCloudBackupService : BackgroundService
{
    private const int DefaultBatchSize = 10;
    private const int DefaultMaxRetryAttempts = 8;

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityMediaCloudBackupService> _logger;

    public IdentityMediaCloudBackupService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<IdentityMediaCloudBackupService> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    private int PollIntervalMinutes => Math.Max(
        5,
        _configuration.GetValue<int?>("IdentityMediaBackup:PollIntervalMinutes") ?? 15);

    private int BatchSize => Math.Clamp(
        _configuration.GetValue<int?>("IdentityMediaBackup:BatchSize") ?? DefaultBatchSize,
        1,
        25);

    private int MaxRetryAttempts => Math.Max(
        1,
        _configuration.GetValue<int?>("IdentityMediaBackup:MaxRetryAttempts") ?? DefaultMaxRetryAttempts);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackUpDueMediaAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Identity-media cloud backup cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(PollIntervalMinutes), stoppingToken);
        }
    }

    private async Task BackUpDueMediaAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var storage = scope.ServiceProvider.GetRequiredService<PrivateIdentityMediaStorage>();
        var now = DateTime.UtcNow;

        var dueDocuments = await db.UserIdentityDocuments
            .Include(document => document.User)
            .Include(document => document.FaceVerifications)
            .Where(document =>
                document.User != null &&
                document.User.ApprovalStatus == "Pending" &&
                document.CloudinaryUploadAttempts < MaxRetryAttempts &&
                (document.CloudinaryNextRetryAt == null || document.CloudinaryNextRetryAt <= now) &&
                ((document.IdPhotoPath != null && document.IdPhotoCloudinaryPublicId == null) ||
                 document.FaceVerifications.Any(face =>
                     face.LiveSelfiePath != null &&
                     face.LiveSelfieCloudinaryPublicId == null)))
            .OrderBy(document => document.CloudinaryNextRetryAt ?? document.UploadedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var document in dueDocuments)
        {
            var errors = new List<string>();

            if (NeedsCloudCopy(document.IdPhotoPath, document.IdPhotoCloudinaryPublicId))
            {
                try
                {
                    var idPath = GetSafeLocalPath(IdentityDocumentStorage.IdsFolder(environment), document.IdPhotoPath);
                    var uploadedId = await storage.UploadLocalFileAsync(idPath!, "id");
                    document.IdPhotoCloudinaryPublicId = uploadedId.PublicId;
                    document.IdPhotoCloudinaryFormat = uploadedId.Format;
                }
                catch (Exception ex)
                {
                    errors.Add("ID image backup failed.");
                    _logger.LogWarning(ex, "Private ID backup failed for identity document {DocumentId}.", document.Id);
                }
            }

            foreach (var face in document.FaceVerifications
                         .Where(face => NeedsCloudCopy(face.LiveSelfiePath, face.LiveSelfieCloudinaryPublicId)))
            {
                try
                {
                    var selfiePath = GetSafeLocalPath(IdentityDocumentStorage.SelfiesFolder(environment), face.LiveSelfiePath);
                    var uploadedSelfie = await storage.UploadLocalFileAsync(selfiePath!, "selfie");
                    face.LiveSelfieCloudinaryPublicId = uploadedSelfie.PublicId;
                    face.LiveSelfieCloudinaryFormat = uploadedSelfie.Format;
                }
                catch (Exception ex)
                {
                    errors.Add("Selfie backup failed.");
                    _logger.LogWarning(ex, "Private selfie backup failed for identity document {DocumentId}.", document.Id);
                }
            }

            var backupComplete = !NeedsCloudCopy(document.IdPhotoPath, document.IdPhotoCloudinaryPublicId) &&
                                 document.FaceVerifications.All(face =>
                                     !NeedsCloudCopy(face.LiveSelfiePath, face.LiveSelfieCloudinaryPublicId));

            if (backupComplete)
            {
                document.CloudinaryUploadAttempts = 0;
                document.CloudinaryNextRetryAt = null;
                document.CloudinaryLastUploadError = null;
            }
            else
            {
                document.CloudinaryUploadAttempts++;
                document.CloudinaryNextRetryAt = now.Add(GetRetryDelay(document.CloudinaryUploadAttempts));
                document.CloudinaryLastUploadError = string.Join(" ", errors).Truncate(500);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool NeedsCloudCopy(string? localFileName, string? cloudinaryPublicId) =>
        !string.IsNullOrWhiteSpace(localFileName) && string.IsNullOrWhiteSpace(cloudinaryPublicId);

    private static string GetSafeLocalPath(string folder, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidOperationException("The protected identity-media reference is invalid.");

        return Path.Combine(folder, fileName);
    }

    private static TimeSpan GetRetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromMinutes(5),
        2 => TimeSpan.FromMinutes(15),
        3 => TimeSpan.FromHours(1),
        4 => TimeSpan.FromHours(4),
        _ => TimeSpan.FromHours(12)
    };
}

internal static class IdentityMediaStringExtensions
{
    public static string Truncate(this string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
