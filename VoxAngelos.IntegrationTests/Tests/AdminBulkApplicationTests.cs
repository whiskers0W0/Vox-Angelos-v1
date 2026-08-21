using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-23 — Admin/UserApplications page (bulk action) -> UserManager (Identity, looped
/// per selected account) -> SensitiveMediaRetentionService -> EmailSender, approving
/// several pending applications in a single submit.
/// </summary>
[Collection("VoxAngelos App")]
public class AdminBulkApplicationTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT23_AdminBulkApprovesMultiplePendingApplications()
    {
        var userIds = new List<string>();
        using (var setupScope = identity.NewScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            for (var i = 0; i < 2; i++)
            {
                var suffix = Guid.NewGuid().ToString("N")[..8];
                var user = new ApplicationUser
                {
                    UserName = $"it-bulk-{suffix}@example.test",
                    Email = $"it-bulk-{suffix}@example.test",
                    EmailConfirmed = true,
                    ApprovalStatus = "Pending",
                    CreatedAt = DateTime.UtcNow
                };
                var result = await userManager.CreateAsync(user, "TestPass123!");
                Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(user, "User");
                userIds.Add(user.Id);
            }
        }

        var admin = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);

        var token = await admin.Client.GetAntiforgeryTokenAsync("/Admin/UserApplications");
        using var content = new FormUrlEncodedContent(
            userIds.Select(id => new KeyValuePair<string, string>("userIds", id))
                .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token)));
        var response = await admin.Client.PostAsync("/Admin/UserApplications?handler=BulkApprove", content);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvedCount = await db.Users.CountAsync(u => userIds.Contains(u.Id) && u.ApprovalStatus == "Approved");
        Assert.Equal(userIds.Count, approvedCount);
    }
}
