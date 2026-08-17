using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-12 / IT-13 — LGU/ReviewRecommendations page -> ApplicationDbContext (atomic,
/// status-guarded ExecuteUpdate) -> UserNotifications (citizen) -> EmailSender ->
/// FeedHub (SignalR "PostPublished") for the approve/publish and reject decisions.
/// </summary>
[Collection("VoxAngelos App")]
public class RecommendationReviewTests(IdentityTestServices identity)
{
    private async Task<int> CreatePendingRecommendationAsync(string label)
    {
        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var citizen = await db.Users.SingleAsync(u => u.Email == TestConfig.CitizenEmail);

        var recommendation = new Recommendation
        {
            CitizenId = citizen.Id,
            Justification = "Integration test fixture.",
            Category = "Urban Planning & Public Spaces",
            AssignedOffice = TestConfig.LguDepartment,
            Title = $"IT-{label} recommendation {Guid.NewGuid():N}",
            Location = "Test location",
            Description = "Integration test recommendation description.",
            Beneficiaries = "Test beneficiaries",
            EstimatedPeopleAffected = 10,
            Status = "Pending",
            SubmittedAt = DateTime.UtcNow
        };
        db.Recommendations.Add(recommendation);
        await db.SaveChangesAsync();
        return recommendation.Id;
    }

    [Fact]
    public async Task IT12_LguApprovesRecommendation_PublishesAndNotifiesCitizen()
    {
        var recommendationId = await CreatePendingRecommendationAsync("12-approve");
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await lgu.Client.PostFormAsync(
            "/LGU/ReviewRecommendations",
            handler: "Approve",
            fields: new Dictionary<string, string>
            {
                ["recommendationId"] = recommendationId.ToString(),
                ["lguNotes"] = "Approved for the integration test."
            });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recommendation = await db.Recommendations.SingleAsync(r => r.Id == recommendationId);
        Assert.Equal("Published", recommendation.Status);
        Assert.NotNull(recommendation.ReviewedByLguId);
        Assert.NotNull(recommendation.ReviewedAt);

        var notification = await db.UserNotifications
            .Where(n => n.RecipientUserId == recommendation.CitizenId && n.NotificationType == "RecommendationUpdate")
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task IT13_LguRejectsRecommendation_UpdatesStatusAndNotifiesCitizen()
    {
        var recommendationId = await CreatePendingRecommendationAsync("13-reject");
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        const string notes = "Does not fall under this office's mandate.";
        var response = await lgu.Client.PostFormAsync(
            "/LGU/ReviewRecommendations",
            handler: "Reject",
            fields: new Dictionary<string, string>
            {
                ["recommendationId"] = recommendationId.ToString(),
                ["lguNotes"] = notes
            });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recommendation = await db.Recommendations.SingleAsync(r => r.Id == recommendationId);
        Assert.Equal("Rejected", recommendation.Status);
        Assert.Equal(notes, recommendation.LguNotes);

        var notification = await db.UserNotifications
            .Where(n => n.RecipientUserId == recommendation.CitizenId && n.NotificationType == "RecommendationUpdate")
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(notification);
    }
}
