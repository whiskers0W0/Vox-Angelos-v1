using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-15 / IT-16 — LGU/Index page -> ApplicationDbContext (atomic status-guarded
/// ExecuteUpdate) -> ConcernTimelineEvent -> UserNotifications (citizen) -> EmailSender,
/// and the "confirm auto-classified category is correct" path -> ConcernClassificationService
/// feedback log. (Note: the direct LGU-to-LGU reassign handler on this page is marked
/// [NonHandler] and unreachable — the app deliberately moved that responsibility to
/// Admin/NlpAccuracy, exercised separately by IT-22.)
/// </summary>
[Collection("VoxAngelos App")]
public class ConcernManagementTests(IdentityTestServices identity)
{
    private async Task<int> CreateUnresolvedConcernAsync(string label, string category)
    {
        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var citizen = await db.Users.SingleAsync(u => u.Email == TestConfig.CitizenEmail);

        var concern = new Concern
        {
            CitizenId = citizen.Id,
            Description = $"IT-{label} concern fixture {Guid.NewGuid():N}",
            Category = category,
            Status = "Unresolved",
            LocationName = "Test location",
            SubmittedAt = DateTime.UtcNow
        };
        db.Concerns.Add(concern);
        await db.SaveChangesAsync();
        return concern.Id;
    }

    [Fact]
    public async Task IT15_LguUpdatesConcernStatus_WritesTimelineAndNotifiesCitizen()
    {
        var concernId = await CreateUnresolvedConcernAsync("15", TestConfig.LguDepartment);
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await lgu.Client.PostFormAsync(
            "/LGU/Index",
            handler: "UpdateStatus",
            fields: new Dictionary<string, string>
            {
                ["concernId"] = concernId.ToString(),
                ["status"] = "In Progress",
                ["notes"] = "Our office is reviewing this concern."
            });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var concern = await db.Concerns.Include(c => c.TimelineEvents).SingleAsync(c => c.Id == concernId);
        Assert.Equal("In Progress", concern.Status);
        Assert.Contains(concern.TimelineEvents, e => e.EventType == "Status Updated");

        var notification = await db.UserNotifications
            .Where(n => n.RecipientUserId == concern.CitizenId && n.NotificationType == "ConcernUpdate")
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task IT16_LguConfirmsAutoClassifiedCategory_RecordsPositiveFeedback()
    {
        var concernId = await CreateUnresolvedConcernAsync("16", TestConfig.LguDepartment);
        var lgu = await LoginFlow.LoginAsync(identity, TestConfig.LguEmail, TestConfig.LguPassword);

        var response = await lgu.Client.PostFormAsync(
            "/LGU/Index",
            handler: "ConfirmCategory",
            fields: new Dictionary<string, string> { ["concernId"] = concernId.ToString() });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var correction = await db.ClassificationCorrections.SingleOrDefaultAsync(c => c.ConcernId == concernId);
        Assert.NotNull(correction);
        Assert.True(correction!.WasCorrect);
        Assert.Equal(TestConfig.LguDepartment, correction.CorrectedCategory);
    }
}
