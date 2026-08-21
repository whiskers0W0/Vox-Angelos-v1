using Microsoft.Extensions.Caching.Memory;

namespace VoxAngelos.Services;

public sealed record FaceLivenessLimitResult(bool Allowed, string? Error = null, int RetryAfterSeconds = 0);

/// <summary>
/// Prevents accidental or abusive creation of paid AWS Face Liveness sessions.
/// This guard is intentionally checked before calling CreateFaceLivenessSession.
/// </summary>
public sealed class FaceLivenessUsageGuard(IMemoryCache cache, IConfiguration configuration)
{
    private readonly object _gate = new();

    private int PerClientHourlyLimit => Math.Max(1,
        configuration.GetValue("AWS:UsageLimits:PerClientHourlySessions", 3));

    private int PerIpHourlyLimit => Math.Max(1,
        configuration.GetValue("AWS:UsageLimits:PerIpHourlySessions", 10));

    private int DailyLimit => Math.Max(1,
        configuration.GetValue("AWS:UsageLimits:DailySessions", 100));

    private int ActiveSessionMinutes => Math.Max(1,
        configuration.GetValue("AWS:UsageLimits:ActiveSessionMinutes", 5));

    public FaceLivenessLimitResult TryBegin(string clientKey, string ipKey)
    {
        var now = DateTimeOffset.UtcNow;
        var clientHourKey = $"face-liveness:client-hour:{clientKey}";
        var ipHourKey = $"face-liveness:ip-hour:{ipKey}";
        var dayKey = $"face-liveness:day:{now:yyyy-MM-dd}";
        var activeKey = $"face-liveness:active:{clientKey}";

        lock (_gate)
        {
            if (cache.TryGetValue(activeKey, out _))
                return new(false, "A face check is already active. Finish it or wait a few minutes before trying again.", 60);

            var clientHourly = cache.GetOrCreate(clientHourKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return 0;
            });

            if (clientHourly >= PerClientHourlyLimit)
                return new(false, "You have reached the face-check attempt limit. Please try again in one hour.", 3600);

            var ipHourly = cache.GetOrCreate(ipHourKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return 0;
            });

            if (ipHourly >= PerIpHourlyLimit)
                return new(false, "Too many face-check attempts. Please try again in one hour.", 3600);

            var daily = cache.GetOrCreate(dayKey, entry =>
            {
                entry.AbsoluteExpiration = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                return 0;
            });

            if (daily >= DailyLimit)
                return new(false, "Face verification has reached today's safety limit. Please try again tomorrow.",
                    (int)Math.Max(60, (now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime).TotalSeconds));

            cache.Set(clientHourKey, clientHourly + 1, TimeSpan.FromHours(1));
            cache.Set(ipHourKey, ipHourly + 1, TimeSpan.FromHours(1));
            cache.Set(dayKey, daily + 1,
                new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero));
            cache.Set(activeKey, true, TimeSpan.FromMinutes(ActiveSessionMinutes));
            return new(true);
        }
    }

    public void End(string clientKey) => cache.Remove($"face-liveness:active:{clientKey}");
}
