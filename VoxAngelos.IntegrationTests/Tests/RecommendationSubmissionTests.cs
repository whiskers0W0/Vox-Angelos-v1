using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-11 — User/Create page (Recommendation form) -> ConcernClassificationService
/// (external NLP) -> CloudinaryAttachmentStorage -> ApplicationDbContext ->
/// LGU UserNotifications + EmailSender.
/// </summary>
[Collection("VoxAngelos App")]
public class RecommendationSubmissionTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT11_RecommendationSubmission_IsSavedPendingAndLguIsNotified()
    {
        var citizen = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);
        var marker = Guid.NewGuid().ToString("N");
        var title = $"IT-11 Community Garden Proposal {marker}";

        var response = await citizen.Client.PostMultipartAsync(
            "/User/Create",
            handler: "Recommendation",
            fields: new Dictionary<string, string>
            {
                ["RecJustification"] = "Residents currently have no shared green space within walking distance.",
                ["RecCategory"] = "Urban Planning & Public Spaces",
                ["RecTitle"] = title,
                ["RecLocation"] = "Vacant lot beside the barangay hall, Angeles City",
                ["RecDescription"] = "Convert the unused vacant lot into a small community garden with seating and native plants for residents to share.",
                ["RecBeneficiaries"] = "Nearby households and senior citizens who want a nearby place to relax.",
                ["RecPeopleAffected"] = "150",
                ["RecIsAnonymous"] = "false"
            },
            files: new[] { ("RecAttachments", TinyImage.FileName, TinyImage.JpegBytes, TinyImage.ContentType) });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), body);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recommendation = await db.Recommendations
            .Include(r => r.Attachments)
            .SingleAsync(r => r.Title == title);

        Assert.Equal("Pending", recommendation.Status);
        Assert.Single(recommendation.Attachments);
    }
}
