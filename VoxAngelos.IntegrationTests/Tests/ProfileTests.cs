using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-25 — User/Profile page -> UserManager.UpdateAsync (Identity), selecting one of
/// the built-in animal avatars.
/// </summary>
[Collection("VoxAngelos App")]
public class ProfileTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT25_CitizenUpdatesProfileAvatar()
    {
        var citizen = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        var response = await citizen.Client.PostFormAsync(
            "/User/Profile",
            handler: "UpdateAvatar",
            fields: new Dictionary<string, string> { ["SelectedAvatar"] = "owl" });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == TestConfig.CitizenEmail);
        Assert.Equal("/images/avatars/owl.png", user.ProfilePhotoUrl);
    }
}
