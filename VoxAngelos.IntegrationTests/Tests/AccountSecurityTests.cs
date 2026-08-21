using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-17 / IT-18 — User/AccountSecurity page -> UserManager (password check) -> OTP
/// generation/verification (Email + SecurityStamp refresh) -> EmailSender, for the
/// password-change and email-change flows. Uses the second seeded citizen (maria) so
/// password mutation doesn't disturb other tests that log in as juan, and restores the
/// original password afterward so the suite stays repeatable across reruns.
/// </summary>
[Collection("VoxAngelos App")]
public class AccountSecurityTests(IdentityTestServices identity)
{
    private async Task<(HttpClient Client, string UserId)> LoginSecondCitizenAsync()
    {
        var login = await LoginFlow.LoginAsync(identity, TestConfig.SecondCitizenEmail, TestConfig.SecondCitizenPassword);
        var user = await identity.GetUserByEmailAsync(TestConfig.SecondCitizenEmail);
        return (login.Client, user.Id);
    }

    private async Task ChangePasswordAsync(HttpClient client, string userId, string currentPassword, string newPassword)
    {
        var requestResponse = await client.PostFormAsync(
            "/User/AccountSecurity",
            handler: "ChangePassword",
            fields: new Dictionary<string, string>
            {
                ["PasswordInput.CurrentPassword"] = currentPassword,
                ["PasswordInput.NewPassword"] = newPassword,
                ["PasswordInput.ConfirmPassword"] = newPassword
            });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, requestResponse.StatusCode);

        var otp = await identity.GenerateEmailOtpAsync(userId);
        var verifyResponse = await client.PostFormAsync(
            "/User/AccountSecurity",
            handler: "VerifyOtp",
            fields: new Dictionary<string, string> { ["OtpInput.Code"] = otp });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task IT17_CitizenChangesPassword_RequiresOtpAndUpdatesCredentials()
    {
        const string temporaryPassword = "TempPass456!";
        var (client, userId) = await LoginSecondCitizenAsync();

        await ChangePasswordAsync(client, userId, TestConfig.SecondCitizenPassword, temporaryPassword);

        using (var scope = identity.NewScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.True(await userManager.CheckPasswordAsync(user!, temporaryPassword));
        }

        // Restore the original password (a fresh OTP round-trip) so the account is
        // left exactly as the rest of the suite — and future reruns — expect it.
        var restoreLogin = await LoginFlow.LoginAsync(identity, TestConfig.SecondCitizenEmail, temporaryPassword);
        await ChangePasswordAsync(restoreLogin.Client, userId, temporaryPassword, TestConfig.SecondCitizenPassword);

        using (var scope = identity.NewScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.True(await userManager.CheckPasswordAsync(user!, TestConfig.SecondCitizenPassword));
        }
    }

    [Fact]
    public async Task IT18_CitizenChangesEmail_RequiresOtpThenConfirmationLink()
    {
        var (client, userId) = await LoginSecondCitizenAsync();
        var temporaryEmail = $"it-emailchange-{Guid.NewGuid():N}@example.test";
        var logMarker = AppLogReader.CurrentLength();

        var requestResponse = await client.PostFormAsync(
            "/User/AccountSecurity",
            handler: "RequestEmailChange",
            fields: new Dictionary<string, string>
            {
                ["EmailInput.CurrentPassword"] = TestConfig.SecondCitizenPassword,
                ["EmailInput.NewEmail"] = temporaryEmail
            });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, requestResponse.StatusCode);

        var otp = await identity.GenerateEmailOtpAsync(userId);
        var verifyResponse = await client.PostFormAsync(
            "/User/AccountSecurity",
            handler: "VerifyOtp",
            fields: new Dictionary<string, string> { ["OtpInput.Code"] = otp });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, verifyResponse.StatusCode);

        // Email must NOT change yet — only the OTP-gated request has been verified;
        // the swap itself waits for confirmation-link ownership proof of the new address.
        using (var scope = identity.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal(TestConfig.SecondCitizenEmail, user.Email);
        }

        var confirmationUrl = await AppLogReader.WaitForConfirmationLinkAsync(
            logMarker, temporaryEmail, TimeSpan.FromSeconds(15));
        var confirmResponse = await client.GetAsync(new Uri(confirmationUrl).PathAndQuery);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, confirmResponse.StatusCode);

        using (var scope = identity.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal(temporaryEmail, user.Email, ignoreCase: true);
        }

        // Restore the original email directly (bypassing OTP) so later/rerun tests
        // that log in with maria@gmail.com keep working.
        using (var scope = identity.NewScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            user!.Email = TestConfig.SecondCitizenEmail;
            user.NormalizedEmail = TestConfig.SecondCitizenEmail.ToUpperInvariant();
            user.UserName = TestConfig.SecondCitizenEmail;
            user.NormalizedUserName = TestConfig.SecondCitizenEmail.ToUpperInvariant();
            await userManager.UpdateAsync(user);
        }
    }
}
