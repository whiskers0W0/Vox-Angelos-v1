using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace VoxAngelos.Services;

public sealed record CloudinaryAttachmentUpload(string FilePath, string FileType);

/// <summary>
/// Stores public concern and recommendation attachments in Cloudinary instead of
/// the web server's local uploads folder. Identity documents and selfies are not
/// handled by this service because they require a separate private-storage policy.
/// </summary>
public sealed class CloudinaryAttachmentStorage
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".webm", ".mkv"
    };

    private readonly IConfiguration _configuration;

    public CloudinaryAttachmentStorage(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<CloudinaryAttachmentUpload> UploadAsync(IFormFile file, string submissionType)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length <= 0)
            throw new InvalidOperationException("The attachment is empty.");

        var folder = submissionType switch
        {
            "concerns" => "voxangelos/concerns",
            "recommendations" => "voxangelos/recommendations",
            _ => throw new ArgumentException("Unsupported attachment folder.", nameof(submissionType))
        };

        var fileType = GetFileType(file);
        var publicId = Guid.NewGuid().ToString("N");
        var cloudinary = CreateCloudinary();
        var isPdf = IsPdf(file);

        await using var stream = file.OpenReadStream();
        var fileDescription = new FileDescription(file.FileName, stream);

        UploadResult uploadResult = fileType switch
        {
            "image" => await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = fileDescription,
                Folder = folder,
                PublicId = publicId,
                Overwrite = false
            }),
            "video" => await cloudinary.UploadAsync(new VideoUploadParams
            {
                File = fileDescription,
                Folder = folder,
                PublicId = publicId,
                Overwrite = false
            }),
            // Cloudinary can deliver and preview PDFs when they are uploaded as
            // image assets. They remain "document" attachments in VoxAngelos.
            _ when isPdf => await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = fileDescription,
                Folder = folder,
                PublicId = publicId,
                Overwrite = false
            }),
            _ => await cloudinary.UploadAsync(new RawUploadParams
            {
                File = fileDescription,
                Folder = folder,
                PublicId = publicId,
                Overwrite = false
            })
        };

        if (uploadResult.Error is not null)
            throw new InvalidOperationException($"Cloudinary could not upload the attachment: {uploadResult.Error.Message}");

        if (uploadResult.SecureUrl is null)
            throw new InvalidOperationException("Cloudinary did not return a secure attachment URL.");

        return new CloudinaryAttachmentUpload(uploadResult.SecureUrl.ToString(), fileType);
    }

    /// <summary>
    /// Deletes a public concern or recommendation attachment from Cloudinary.
    /// Legacy local paths are deliberately ignored and treated as already handled.
    /// </summary>
    public async Task<bool> DeleteAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            !TryGetCloudinaryAsset(filePath, out var publicId, out var resourceType))
        {
            return true;
        }

        var result = await CreateCloudinary().DestroyAsync(new DeletionParams(publicId)
        {
            ResourceType = resourceType,
            Invalidate = true
        });

        return string.Equals(result.Result, "ok", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.Result, "not found", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.Result, "not_found", StringComparison.OrdinalIgnoreCase);
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

    private static string GetFileType(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);

        if (file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            VideoExtensions.Contains(extension))
        {
            return "video";
        }

        if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            ImageExtensions.Contains(extension))
        {
            return "image";
        }

        return "document";
    }

    private static bool IsPdf(IFormFile file)
    {
        return file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCloudinaryAsset(
        string filePath,
        out string publicId,
        out ResourceType resourceType)
    {
        publicId = string.Empty;
        resourceType = ResourceType.Image;

        if (!Uri.TryCreate(filePath, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var uploadIndex = Array.FindIndex(
            segments,
            segment => segment.Equals("upload", StringComparison.OrdinalIgnoreCase));

        if (uploadIndex < 1 || uploadIndex + 1 >= segments.Length)
            return false;

        resourceType = segments[uploadIndex - 1].ToLowerInvariant() switch
        {
            "image" => ResourceType.Image,
            "video" => ResourceType.Video,
            "raw" => ResourceType.Raw,
            _ => ResourceType.Image
        };

        var publicIdStart = uploadIndex + 1;
        if (publicIdStart < segments.Length &&
            segments[publicIdStart].Length > 1 &&
            segments[publicIdStart][0] == 'v' &&
            segments[publicIdStart][1..].All(char.IsDigit))
        {
            publicIdStart++;
        }

        if (publicIdStart >= segments.Length)
            return false;

        publicId = string.Join('/', segments[publicIdStart..]);

        if (!publicId.StartsWith("voxangelos/concerns/", StringComparison.OrdinalIgnoreCase) &&
            !publicId.StartsWith("voxangelos/recommendations/", StringComparison.OrdinalIgnoreCase))
        {
            publicId = string.Empty;
            return false;
        }

        // Image and video delivery URLs include a format extension that is not
        // part of the Cloudinary public ID. Raw-file public IDs may include it.
        if (resourceType is ResourceType.Image or ResourceType.Video)
        {
            var extension = Path.GetExtension(publicId);
            if (!string.IsNullOrWhiteSpace(extension))
                publicId = publicId[..^extension.Length];
        }

        return !string.IsNullOrWhiteSpace(publicId);
    }
}
