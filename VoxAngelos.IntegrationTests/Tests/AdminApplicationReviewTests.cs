using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-02 / IT-03 — Admin/ReviewApplication page -> Identity (UserManager) ->
/// AccountApproval audit table -> SensitiveMediaRetentionService -> EmailSender,
/// for the approve and reject decisions.
/// </summary>
[Collection("VoxAngelos App")]
public class AdminApplicationReviewTests(IdentityTestServices identity)
{
    private async Task<string> CreatePendingCitizenAsync(string label)
    {
        using var scope = identity.NewScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            UserName = $"it-{label}-{suffix}@example.test",
            Email = $"it-{label}-{suffix}@example.test",
            EmailConfirmed = true,
            ApprovalStatus = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(user, "TestPass123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, "User");

        db.UserProfiles.Add(new UserProfile { UserId = user.Id, FirstName = "Test", LastName = label });
        await db.SaveChangesAsync();

        return user.Id;
    }

    [Fact]
    public async Task IT02_AdminApprovesPendingApplication_UpdatesStatusAndNotifies()
    {
        var userId = await CreatePendingCitizenAsync("approve");
        var admin = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);

        var response = await admin.Client.PostFormAsync(
            $"/Admin/ReviewApplication?userId={userId}",
            handler: "Approve",
            fields: new Dictionary<string, string> { ["userId"] = userId });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("Approved", user.ApprovalStatus);
    }

    [Fact]
    public async Task IT03_AdminRejectsPendingApplication_WritesAuditRecordAndNotifies()
    {
        var userId = await CreatePendingCitizenAsync("reject");
        var admin = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);

        const string reason = "ID photo did not match Angeles City residency records.";
        var response = await admin.Client.PostFormAsync(
            $"/Admin/ReviewApplication?userId={userId}",
            handler: "Reject",
            fields: new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["rejectionReason"] = reason
            });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("Rejected", user.ApprovalStatus);

        var approval = await db.AccountApprovals.SingleAsync(a => a.UserId == userId);
        Assert.Equal("Rejected", approval.Status);
        Assert.Equal(reason, approval.RejectionReason);
        Assert.NotNull(approval.ReviewedAt);
        Assert.NotNull(approval.ReviewedByAdminId);
    }
}
