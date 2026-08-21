using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-20 / IT-21 — LGU/Heatmap (PostGIS-backed concern density query) and
/// LGU/Discover (published-recommendation feed + RecommendationRatingService reads)
/// both load successfully for an authenticated LGU session.
/// </summary>
[Collection("VoxAngelos App")]
public class LguPublicPagesTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT20_LguHeatmap_LoadsConcernDensityData()
    {
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await lgu.Client.GetAsync("/LGU/Heatmap");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IT21_LguDiscoverFeed_LoadsPublishedRecommendations()
    {
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await lgu.Client.GetAsync("/LGU/Discover");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
