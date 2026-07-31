using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace VoxAngelos.Services;

public sealed record PrivateIdentityMediaUpload(string PublicId, string Format);

/// <summary>
/// Stores government IDs and live selfies as private Cloudinary images. This is
/// intentionally separate from public citizen-submission attachments.
/// </summary>
public sealed class PrivateIdentityMediaStorage
{
    private const int MaximumUploadAttempts = 3;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };

    private readonly IConfiguration _configuration;

    public PrivateIdentityMediaStorage(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<PrivateIdentityMediaUpload> UploadAsync(IFormFile file, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length <= 0)
            throw new InvalidOperationException("The identity image is empty.");

        var extension = Path.GetExtension(file.FileName);
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            !AllowedImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Only JPG and PNG identity images are allowed.");
        }

        return await UploadWithRetriesAsync(file.OpenReadStream, file.FileName, mediaType);
    }

    /// <summary>
    /// Retries a private upload from the protected local copy created during
    /// registration when Cloudinary was temporarily unavailable.
    /// </summary>
    public async Task<PrivateIdentityMediaUpload> UploadLocalFileAsync(string fullPath, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            throw new FileNotFoundException("The protected identity image could not be found.", fullPath);

        var fileName = Path.GetFileName(fullPath);
        if (!AllowedImageExtensions.Contains(Path.GetExtension(fileName)))
            throw new InvalidOperationException("Only JPG and PNG identity images are allowed.");

        return await UploadWithRetriesAsync(() => File.OpenRead(fullPath), fileName, mediaType);
    }

    private async Task<PrivateIdentityMediaUpload> UploadWithRetriesAsync(
        Func<Stream> openStream,
        string fileName,
        string mediaType)
    {
        var folder = mediaType switch
        {
            "id" => "voxangelos/identity/ids",
            "selfie" => "voxangelos/identity/selfies",
            _ => throw new ArgumentException("Unsupported identity media type.", nameof(mediaType))
        };

        var cloudinary = CreateCloudinary();

        for (var attempt = 1; attempt <= MaximumUploadAttempts; attempt++)
        {
            await using var stream = openStream();

            try
            {
                var result = await cloudinary.UploadAsync(new ImageUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = folder,
                    PublicId = Guid.NewGuid().ToString("N"),
                    Type = "private",
                    Overwrite = false
                });

                if (result.Error is null)
                {
                    if (string.IsNullOrWhiteSpace(result.PublicId) || string.IsNullOrWhiteSpace(result.Format))
                    {
                        throw new InvalidOperationException(
                            "Cloudinary did not return the identity image details.");
                    }

                    return new PrivateIdentityMediaUpload(result.PublicId, result.Format);
                }

                if (attempt < MaximumUploadAttempts && IsTransientStatusCode((int)result.StatusCode))
                {
                    await DelayBeforeRetryAsync(attempt);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Cloudinary could not upload the identity image: {result.Error.Message}");
            }
            catch (Exception ex) when (
                attempt < MaximumUploadAttempts && IsTransientException(ex))
            {
                await DelayBeforeRetryAsync(attempt);
            }
        }

        throw new InvalidOperationException(
            "Cloudinary could not upload the identity image after multiple attempts.");
    }

    private static bool IsTransientStatusCode(int statusCode) =>
        statusCode is 408 or 429 || statusCode >= 500;

    private static bool IsTransientException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException ||
        (exception.InnerException is not null && IsTransientException(exception.InnerException));

    private static Task DelayBeforeRetryAsync(int failedAttempt) =>
        Task.Delay(TimeSpan.FromSeconds(failedAttempt == 1 ? 1 : 3));

    private Cloudinary CreateCloudinary()
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrWhiteSpace(cloudName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary credentials are missing. Please contact the system administrator.");
        }

        var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
        cloudinary.Api.Secure = true;
        return cloudinary;
    }
}
