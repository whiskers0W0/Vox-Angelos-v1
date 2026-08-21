using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-19 — User/Index (Discover feed) page -> RecommendationRatingService (atomic
/// aggregate recompute) -> ApplicationDbContext (RecommendationRating upsert, unique
/// per citizen+recommendation) -> FeedHub (SignalR "RatingUpdated").
/// </summary>
[Collection("VoxAngelos App")]
public class RecommendationRatingTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT19_CitizenRatesPublishedRecommendation_UpdatesAggregates()
    {
        int recommendationId;
        using (var scope = identity.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var author = await db.Users.SingleAsync(u => u.Email == TestConfig.SecondCitizenEmail);

            var recommendation = new Recommendation
            {
                CitizenId = author.Id,
                Justification = "Integration test fixture.",
                Category = "Urban Planning & Public Spaces",
                AssignedOffice = TestConfig.LguDepartment,
                Title = $"IT-19 recommendation {Guid.NewGuid():N}",
                Location = "Test location",
                Description = "Integration test recommendation description.",
                Beneficiaries = "Test beneficiaries",
                EstimatedPeopleAffected = 10,
                Status = "Published",
                SubmittedAt = DateTime.UtcNow,
                ReviewedAt = DateTime.UtcNow
            };
            db.Recommendations.Add(recommendation);
            await db.SaveChangesAsync();
            recommendationId = recommendation.Id;
        }

        // Rated by juan — the recommendation belongs to maria, so this isn't a
        // self-rating attempt (which the handler explicitly rejects).
        var rater = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        var response = await rater.Client.PostFormAsync(
            "/User",
            handler: "Rate",
            fields: new Dictionary<string, string>
            {
                ["recommendationId"] = recommendationId.ToString(),
                ["urgencyStars"] = "5",
                ["relevanceStars"] = "4",
                ["feasibilityStars"] = "5"
            });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), body);

        using var verifyScope = identity.NewScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rating = await verifyDb.RecommendationRatings
            .SingleAsync(r => r.RecommendationId == recommendationId);
        Assert.Equal(5, rating.UrgencyStars);

        var updatedRecommendation = await verifyDb.Recommendations.SingleAsync(r => r.Id == recommendationId);
        Assert.Equal(1, updatedRecommendation.RatingCount);
        Assert.True(updatedRecommendation.CompositeScore > 0);
    }
}
