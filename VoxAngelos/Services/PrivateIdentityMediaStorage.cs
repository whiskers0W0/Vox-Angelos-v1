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

        await using var stream = file.OpenReadStream();
        return await UploadStreamAsync(stream, file.FileName, mediaType);
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

        await using var stream = File.OpenRead(fullPath);
        return await UploadStreamAsync(stream, fileName, mediaType);
    }

    private async Task<PrivateIdentityMediaUpload> UploadStreamAsync(Stream stream, string fileName, string mediaType)
    {

        var folder = mediaType switch
        {
            "id" => "voxangelos/identity/ids",
            "selfie" => "voxangelos/identity/selfies",
            _ => throw new ArgumentException("Unsupported identity media type.", nameof(mediaType))
        };

        var result = await CreateCloudinary().UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(fileName, stream),
            Folder = folder,
            PublicId = Guid.NewGuid().ToString("N"),
            Type = "private",
            Overwrite = false
        });

        if (result.Error is not null)
            throw new InvalidOperationException($"Cloudinary could not upload the identity image: {result.Error.Message}");

        if (string.IsNullOrWhiteSpace(result.PublicId) || string.IsNullOrWhiteSpace(result.Format))
            throw new InvalidOperationException("Cloudinary did not return the identity image details.");

        return new PrivateIdentityMediaUpload(result.PublicId, result.Format);
    }

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
