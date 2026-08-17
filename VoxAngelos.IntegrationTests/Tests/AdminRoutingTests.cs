using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-22 — Admin/NlpAccuracy page -> ApplicationDbContext (status-guarded ExecuteUpdate)
/// -> AdminRoutingAssignment audit log -> LGU UserNotifications, for manually routing an
/// uncategorized submission (SubmissionRouting:AdminTriageUncategorized is enabled in
/// this deployment's appsettings.json, so unclassified concerns wait here for a human).
/// </summary>
[Collection("VoxAngelos App")]
public class AdminRoutingTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT22_AdminAssignsUncategorizedConcern_ToLguDepartment()
    {
        int concernId;
        using (var setupScope = identity.NewScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var citizenUser = await setupDb.Users.SingleAsync(u => u.Email == TestConfig.CitizenEmail);

            var newConcern = new Concern
            {
                CitizenId = citizenUser.Id,
                Description = $"IT-22 uncategorized concern fixture {Guid.NewGuid():N}",
                Category = null,
                Status = "Unresolved",
                SubmittedAt = DateTime.UtcNow
            };
            setupDb.Concerns.Add(newConcern);
            await setupDb.SaveChangesAsync();
            concernId = newConcern.Id;
        }

        var admin = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);

        var response = await admin.Client.PostFormAsync(
            "/Admin/NlpAccuracy",
            handler: "Assign",
            fields: new Dictionary<string, string>
            {
                ["submissionType"] = "Concern",
                ["submissionId"] = concernId.ToString(),
                ["department"] = TestConfig.LguDepartment
            });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var concern = await db.Concerns.SingleAsync(c => c.Id == concernId);
        Assert.Equal(TestConfig.LguDepartment, concern.Category);
        Assert.Equal(TestConfig.LguDepartment, concern.AssignedOffice);

        var assignment = await db.AdminRoutingAssignments
            .SingleOrDefaultAsync(a => a.SubmissionType == "Concern" && a.SubmissionId == concernId);
        Assert.NotNull(assignment);
        Assert.Equal(TestConfig.LguDepartment, assignment!.Department);

        var lguUser = await db.Users.SingleAsync(u => u.Email == TestConfig.LguEmail);
        var notification = await db.UserNotifications
            .Where(n => n.RecipientUserId == lguUser.Id && n.NotificationType == "IncomingConcern")
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(notification);
    }
}
