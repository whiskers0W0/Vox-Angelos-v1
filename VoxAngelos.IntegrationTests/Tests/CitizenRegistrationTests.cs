using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-01 — Identity/Account/Register page -> GeminiOcrService (OCR) ->
/// FaceVerificationService (face match) -> PrivateIdentityMediaStorage (Cloudinary) ->
/// UserManager.CreateAsync -> ApplicationDbContext (UserProfile/UserIdentityDocument/
/// UserOcrVerification/UserFaceVerification) -> Admin UserNotifications.
/// </summary>
[Collection("VoxAngelos App")]
public class CitizenRegistrationTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT01_CitizenRegistration_CreatesPendingAccountWithVerificationRecords()
    {
        var client = HttpClientExtensions.CreateAppClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"it-register-{suffix}@example.test";
        var phone = "09" + new Random().Next(100000000, 999999999);

        var response = await client.PostMultipartAsync(
            "/Identity/Account/Register",
            handler: "CreateAccount",
            fields: new Dictionary<string, string>
            {
                ["Input.FirstName"] = "Integration",
                ["Input.LastName"] = "Test",
                ["Input.PhoneNumber"] = phone,
                ["Input.Email"] = email,
                ["Input.IdType"] = "National ID",
                ["Input.Password"] = "TestPass123!",
                ["Input.ConfirmPassword"] = "TestPass123!"
            },
            files: new[]
            {
                ("Input.IdPhoto", TinyImage.FileName, TinyImage.JpegBytes, TinyImage.ContentType),
                ("Input.SelfiePhoto", TinyImage.FileName, TinyImage.JpegBytes, TinyImage.ContentType)
            });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), body);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal("Pending", user.ApprovalStatus);

        var profile = await db.UserProfiles.SingleAsync(p => p.UserId == user.Id);
        Assert.Equal("Integration", profile.FirstName);

        var identityDoc = await db.UserIdentityDocuments.SingleAsync(d => d.UserId == user.Id);
        Assert.Equal("National ID", identityDoc.IdType);

        // Created unconditionally by RegisterModel regardless of OCR/face-match quality —
        // presence of the row (not its content) is what confirms the OCR/face-match
        // services were actually called during this registration.
        await db.UserOcrVerifications.SingleAsync(o => o.UserId == user.Id);
        await db.UserFaceVerifications.SingleAsync(f => f.UserId == user.Id);

        var adminNotification = await db.UserNotifications
            .Where(n => n.NotificationType == "CitizenApplication" && n.SenderName!.Contains("Integration Test"))
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(adminNotification);
    }
}
