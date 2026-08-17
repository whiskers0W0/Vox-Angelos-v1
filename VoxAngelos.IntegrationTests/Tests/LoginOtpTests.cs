using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-06 / IT-07 / IT-08 — Shared Login page -> Identity (UserManager 2FA token) ->
/// LoginWith2fa page -> SignInManager -> role-based redirect, for each of the three roles.
/// </summary>
[Collection("VoxAngelos App")]
public class LoginOtpTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT06_CitizenLogin_CompletesOtp_RedirectsToUserIndex()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        // Razor Pages routes an Index.cshtml page to its folder root, so
        // RedirectToPage("/User/Index") resolves to "/User", not "/User/Index".
        Assert.StartsWith("/User", result.RedirectLocation);

        var homeResponse = await result.Client.GetAsync("/User/Index");
        Assert.Equal(System.Net.HttpStatusCode.OK, homeResponse.StatusCode);
    }

    [Fact]
    public async Task IT07_AdminLogin_CompletesOtp_RedirectsToAdminIndex()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);

        // Razor Pages routes an Index.cshtml page to its folder root, so
        // RedirectToPage("/Admin/Index") resolves to "/Admin", not "/Admin/Index".
        Assert.StartsWith("/Admin", result.RedirectLocation);

        var homeResponse = await result.Client.GetAsync("/Admin/Index");
        Assert.Equal(System.Net.HttpStatusCode.OK, homeResponse.StatusCode);
    }

    [Fact]
    public async Task IT08_LguLogin_CompletesOtp_RedirectsToLguDashboard()
    {
        var result = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        Assert.Contains("/LGU/Dashboard", result.RedirectLocation, StringComparison.OrdinalIgnoreCase);

        var homeResponse = await result.Client.GetAsync("/LGU/Dashboard");
        Assert.Equal(System.Net.HttpStatusCode.OK, homeResponse.StatusCode);
    }
}
