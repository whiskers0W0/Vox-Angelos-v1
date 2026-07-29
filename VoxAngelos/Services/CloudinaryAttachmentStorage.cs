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
}
