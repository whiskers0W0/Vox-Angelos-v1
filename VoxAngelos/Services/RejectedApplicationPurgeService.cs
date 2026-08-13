using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;

namespace VoxAngelos.Services;

/// <summary>
/// Permanently deletes a citizen account a fixed number of days after it was
/// rejected, per the Data Privacy Act retention policy stated on the Rejected tab
/// of /Admin/UserApplications. Reads the rejection timestamp from AccountApproval
/// (written by ReviewApplicationModel.OnPostRejectAsync), not from the media
/// retention sweep — that one purges photos immediately on review, this one
/// removes the account itself after the retention window.
/// </summary>
public sealed class RejectedApplicationPurgeService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RejectedApplicationPurgeService> _logger;

    public RejectedApplicationPurgeService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<RejectedApplicationPurgeService> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    private int PollIntervalMinutes => Math.Max(
        5,
        _configuration.GetValue<int?>("RejectedApplicationPurge:PollIntervalMinutes") ?? 60);

    private int RetentionDays => Math.Max(
        1,
        _configuration.GetValue<int?>("RejectedApplicationPurge:RetentionDays") ?? 7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredRejectionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rejected-application purge cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(PollIntervalMinutes), stoppingToken);
        }
    }

    private async Task PurgeExpiredRejectionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var mediaRetention = scope.ServiceProvider.GetRequiredService<SensitiveMediaRetentionService>();

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        var expiredUserIds = await db.AccountApprovals
            .Where(a => a.Status == "Rejected" && a.ReviewedAt != null && a.ReviewedAt <= cutoff)
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken);

        var purgedCount = 0;
        foreach (var userId in expiredUserIds)
        {
            var user = await userManager.FindByIdAsync(userId);

            // Only delete if the account is still Rejected — an admin may have
            // reversed the decision by deleting and recreating, or (future-proofing)
            // some other flow may have changed the status since the sweep query ran.
            if (user == null || user.ApprovalStatus != "Rejected")
                continue;

            try
            {
                await mediaRetention.PurgeUserMediaAsync(userId, cancellationToken);

                var result = await userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    purgedCount++;
                }
                else
                {
                    _logger.LogWarning(
                        "Could not delete rejected citizen {UserId} past the retention window: {Errors}",
                        userId,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to purge rejected citizen {UserId}.", userId);
            }
        }

        if (purgedCount > 0)
        {
            _logger.LogInformation(
                "Rejected-application purge deleted {Count} account(s) past the {Days}-day retention window.",
                purgedCount,
                RetentionDays);
        }
    }
}
