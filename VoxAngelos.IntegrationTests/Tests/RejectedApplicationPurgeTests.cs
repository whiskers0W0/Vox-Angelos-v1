using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-04 — RejectedApplicationPurgeService (background hosted service) ->
/// ApplicationDbContext (AccountApproval.ReviewedAt past the retention window) ->
/// SensitiveMediaRetentionService -> UserManager.DeleteAsync. The app under test is
/// launched with RejectedApplicationPurge:PollIntervalMinutes=5 (the service's own
/// floor), so this test waits for a real background sweep instead of invoking any
/// internal method directly.
/// </summary>
[Collection("VoxAngelos App")]
public class RejectedApplicationPurgeTests(IdentityTestServices identity)
{
    [Fact(Timeout = 7 * 60 * 1000)]
    public async Task IT04_RejectedAccountPastRetentionWindow_IsAutomaticallyPurged()
    {
        string userId;
        using (var scope = identity.NewScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var user = new ApplicationUser
            {
                UserName = $"it-purge-{suffix}@example.test",
                Email = $"it-purge-{suffix}@example.test",
                EmailConfirmed = true,
                ApprovalStatus = "Rejected",
                CreatedAt = DateTime.UtcNow.AddDays(-9)
            };
            var result = await userManager.CreateAsync(user, "TestPass123!");
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(user, "User");

            // 8 days ago — one day past the app's configured 7-day retention window.
            db.AccountApprovals.Add(new AccountApproval
            {
                UserId = user.Id,
                Status = "Rejected",
                RejectionReason = "Integration test fixture.",
                RequestedAt = DateTime.UtcNow.AddDays(-9),
                ReviewedAt = DateTime.UtcNow.AddDays(-8)
            });
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var deadline = DateTime.UtcNow.AddMinutes(6);
        ApplicationUser? stillPresent;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            using var scope = identity.NewScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            stillPresent = await userManager.FindByIdAsync(userId);
        }
        while (stillPresent != null && DateTime.UtcNow < deadline);

        Assert.Null(stillPresent);
    }
}
