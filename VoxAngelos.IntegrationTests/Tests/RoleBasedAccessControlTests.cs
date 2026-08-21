using System.Net;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-14 — ASP.NET Core Authorization middleware -> Identity role claims -> Razor
/// Pages folder conventions (AuthorizeFolder("/Admin", "RequireAdminRole") etc. in
/// Program.cs) blocking both anonymous and cross-role access.
/// </summary>
[Collection("VoxAngelos App")]
public class RoleBasedAccessControlTests(IdentityTestServices identity)
{
    [Theory]
    [InlineData("/Admin/Index")]
    [InlineData("/LGU/Dashboard")]
    [InlineData("/User/Concerns")]
    public async Task IT14a_AnonymousRequest_IsRedirectedToLogin(string protectedPage)
    {
        var client = HttpClientExtensions.CreateAppClient(allowAutoRedirect: false);

        var response = await client.GetAsync(protectedPage);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IT14b_CitizenCannotAccessAdminArea()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        var response = await result.Client.GetAsync("/Admin/Index");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IT14c_LguCannotAccessUserArea()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await result.Client.GetAsync("/User/Concerns");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IT14d_CitizenCannotAccessLguArea()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        var response = await result.Client.GetAsync("/LGU/Dashboard");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
