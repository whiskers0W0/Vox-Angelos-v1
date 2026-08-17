using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-24 — User/Concerns page -> ApplicationDbContext, marking a UserNotification as
/// read (the same handler backs the citizen notification bell across the app).
/// </summary>
[Collection("VoxAngelos App")]
public class NotificationTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT24_CitizenMarksNotificationAsRead()
    {
        int notificationId;
        using (var setupScope = identity.NewScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var citizenUser = await setupDb.Users.SingleAsync(u => u.Email == TestConfig.CitizenEmail);

            var newNotification = new UserNotification
            {
                RecipientUserId = citizenUser.Id,
                Title = "IT-24 test notification",
                Message = "Integration test fixture.",
                NotificationType = "ConcernUpdate",
                SenderRole = "LGU",
                SenderName = "Test",
                LinkUrl = "/User/Concerns",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            setupDb.UserNotifications.Add(newNotification);
            await setupDb.SaveChangesAsync();
            notificationId = newNotification.Id;
        }

        var citizen = await LoginFlow.LoginAsync(identity, TestConfig.CitizenEmail, TestConfig.CitizenPassword);

        var response = await citizen.Client.PostFormAsync(
            "/User/Concerns",
            handler: "MarkNotificationRead",
            fields: new Dictionary<string, string> { ["notificationId"] = notificationId.ToString() });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var scope = identity.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notification = await db.UserNotifications.SingleAsync(n => n.Id == notificationId);
        Assert.True(notification.IsRead);
        Assert.NotNull(notification.ReadAt);
    }
}
