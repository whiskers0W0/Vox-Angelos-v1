namespace VoxAngelos.IntegrationTests.TestSupport;

/// <summary>
/// A minimal valid 1x1 JPEG, reused as a stand-in ID photo / selfie / concern
/// attachment so tests exercise the real Cloudinary / OCR / face-match network
/// calls without needing to ship real photo fixtures.
/// </summary>
public static class TinyImage
{
    public static readonly byte[] JpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkI" +
        "CQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/wAALCAABAAEBAREA/8QAFAABAAAAAAAA" +
        "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==");

    public const string FileName = "test-image.jpg";
    public const string ContentType = "image/jpeg";
}
