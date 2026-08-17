using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-09 / IT-10 — User/Create page -> ConcernClassificationService (external NLP) ->
/// CloudinaryAttachmentStorage -> ApplicationDbContext (transactional Concern +
/// ConcernTimelineEvent + ConcernAttachment) -> UrgencyScoreService (PostGIS), and the
/// GeoJSON geofence check that must short-circuit the whole chain for out-of-area pins.
/// </summary>
[Collection("VoxAngelos App")]
public class ConcernSubmissionTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT09_ConcernInsideServiceArea_IsSavedWithTimelineAndAttachment()
    {
        var citizen = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);
        var marker = Guid.NewGuid().ToString("N");
        var description = $"[IT-09 {marker}] Streetlight along the corner has been flickering and going dark at night, creating a safety hazard for pedestrians.";

        var response = await citizen.Client.PostMultipartAsync(
            "/User/Create",
            handler: null,
            fields: new Dictionary<string, string>
            {
                ["Description"] = description,
                ["LocationName"] = "Test Street, Angeles City",
                ["Latitude"] = TestConfig.InsideAngelesLatitude.ToString("G17"),
                ["Longitude"] = TestConfig.InsideAngelesLongitude.ToString("G17")
            },
            files: new[] { ("Attachments", TinyImage.FileName, TinyImage.JpegBytes, TinyImage.ContentType) });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var concern = await db.Concerns
            .Include(c => c.Attachments)
            .Include(c => c.TimelineEvents)
            .SingleAsync(c => c.Description == description);

        Assert.Equal("Unresolved", concern.Status);
        Assert.Single(concern.Attachments);
        Assert.Contains(concern.TimelineEvents, e => e.EventType == "Submitted");
    }

    [Fact]
    public async Task IT10_ConcernOutsideServiceArea_IsRejectedBeforeSaving()
    {
        var citizen = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);
        var marker = Guid.NewGuid().ToString("N");
        var description = $"[IT-10 {marker}] Flooding reported near the intersection after heavy rain, water is knee-deep and blocking traffic.";

        var response = await citizen.Client.PostMultipartAsync(
            "/User/Create",
            handler: null,
            fields: new Dictionary<string, string>
            {
                ["Description"] = description,
                ["LocationName"] = "Outside Service Area",
                ["Latitude"] = TestConfig.OutsideAngelesLatitude.ToString("G17"),
                ["Longitude"] = TestConfig.OutsideAngelesLongitude.ToString("G17")
            },
            files: new[] { ("Attachments", TinyImage.FileName, TinyImage.JpegBytes, TinyImage.ContentType) });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Angeles City", body);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var wasSaved = await db.Concerns.AnyAsync(c => c.Description == description);
        Assert.False(wasSaved);
    }
}
