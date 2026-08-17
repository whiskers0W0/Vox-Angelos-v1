using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoxAngelos.Data;

namespace VoxAngelos.IntegrationTests.TestSupport;

/// <summary>
/// Builds the same Identity/EF stack Program.cs wires up (see
/// tools/SeedTestDataset/Program.cs for the same pattern), pointed at the local test
/// database, so tests can query DB state and regenerate a user's current 2FA email OTP
/// without needing the running app to expose it. Regeneration works because Identity's
/// email token provider derives the code deterministically from the user's SecurityStamp
/// plus the current UTC time window — both processes read/write the same database, so
/// calling GenerateTwoFactorTokenAsync here reproduces the exact code the app generated.
/// </summary>
public sealed class IdentityTestServices : IDisposable
{
    private readonly ServiceProvider _provider;

    public IdentityTestServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(TestConfig.LocalConnectionString, npgsql => npgsql.UseNetTopologySuite()));
        // AddDefaultTokenProviders() registers providers (Default/Authenticator) whose
        // construction requires IDataProtectionProvider even though this helper only
        // ever calls the Email provider — ephemeral in-memory keys are fine here.
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultEmailProvider;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher<ApplicationUser>>();

        _provider = services.BuildServiceProvider();
    }

    /// <summary>Fresh DI scope per call so each test/assertion gets its own DbContext/UserManager instance.</summary>
    public IServiceScope NewScope() => _provider.CreateScope();

    public async Task<ApplicationUser> GetUserByEmailAsync(string email)
    {
        using var scope = NewScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"User '{email}' was not found in the local test database.");
        return user;
    }

    /// <summary>Regenerates the current valid 6-digit login/verification OTP for a user by id.</summary>
    public async Task<string> GenerateEmailOtpAsync(string userId)
    {
        using var scope = NewScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User id '{userId}' was not found.");
        return await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
    }

    public void Dispose() => _provider.Dispose();
}
