using CloudinaryDotNet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services; 
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Cloudinary credentials come from User Secrets locally and environment variables
// in deployment. They are never stored in appsettings.json or committed to Git.
builder.Services.AddSingleton<Cloudinary>(_ =>
{
    var cloudName = builder.Configuration["Cloudinary:CloudName"];
    var apiKey = builder.Configuration["Cloudinary:ApiKey"];
    var apiSecret = builder.Configuration["Cloudinary:ApiSecret"];

    if (string.IsNullOrWhiteSpace(cloudName) ||
        string.IsNullOrWhiteSpace(apiKey) ||
        string.IsNullOrWhiteSpace(apiSecret))
    {
        throw new InvalidOperationException(
            "Cloudinary credentials are missing. Configure Cloudinary:CloudName, Cloudinary:ApiKey, and Cloudinary:ApiSecret.");
    }

    var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
    cloudinary.Api.Secure = true;
    return cloudinary;
});

// Allow the request to reach the recommendation handler. The handler itself
// enforces the 100 MB per-video limit and returns a user-friendly message.
const long maximumUploadRequestSize = 105L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maximumUploadRequestSize;
});

// 1. Database Configuration
var rawUrl = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

string connectionString;
if (rawUrl.StartsWith("postgresql://") || rawUrl.StartsWith("postgres://"))
{
    var uri = new Uri(rawUrl);
    var userInfo = uri.UserInfo.Split(':');
    // Cap the pool well under Render's free-tier Postgres connection limit — with no
    // limit set, Npgsql defaults to 100 and the server starts forcibly resetting
    // connections under moderate concurrent load instead of the pool just queuing.
    connectionString = $"Host={uri.Host};Port={(uri.Port == -1 ? 5432 : uri.Port)};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10;Minimum Pool Size=0";
}
else
{
    connectionString = rawUrl;
}

builder.Services.AddHttpClient(nameof(VoxAngelos.Services.EmailSender));
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, VoxAngelos.Services.EmailSender>();
builder.Services.AddSingleton<VoxAngelos.Services.BackgroundEmailQueue>();
builder.Services.AddSingleton<VoxAngelos.Services.IBackgroundEmailQueue>(provider =>
    provider.GetRequiredService<VoxAngelos.Services.BackgroundEmailQueue>());
builder.Services.AddHostedService(provider =>
    provider.GetRequiredService<VoxAngelos.Services.BackgroundEmailQueue>());

builder.Services.AddHttpClient(nameof(VoxAngelos.Services.SmsSender));
builder.Services.AddTransient<VoxAngelos.Services.ISmsSender, VoxAngelos.Services.SmsSender>();

builder.Services.AddHttpClient(nameof(GeminiOcrService), c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddScoped<GeminiOcrService>();
builder.Services.AddHttpClient(nameof(GeminiConcernClassifierService), c => c.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<GeminiConcernClassifierService>();
builder.Services.AddScoped<HfConcernClassifierService>();
builder.Services.AddScoped<ConcernClassificationService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RegistrationFaceTicketStore>();
builder.Services.AddSingleton<FaceLivenessUsageGuard>();
builder.Services.AddScoped<RecommendationRatingService>();
builder.Services.AddScoped<CloudinaryAttachmentStorage>();
builder.Services.AddScoped<PrivateIdentityMediaStorage>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseNetTopologySuite();
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    }));

// Persist Data Protection keys (antiforgery tokens, auth cookies) to the shared
// Postgres DB instead of local disk — Render's free-tier containers respin on a
// fresh filesystem after idling, which silently invalidates any token/cookie
// already embedded in a page a user has open. The DB survives that.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("VoxAngelos");

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// 2. Identity Configuration
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false; // Set this to true for OTP to be required

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 3;
    options.Lockout.AllowedForNewUsers = true;

    options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultEmailProvider;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders(); // This is the "Engine" that creates the OTP code

// 3. Register the BCrypt Password Hasher
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher<ApplicationUser>>();

// 4. Razor Pages & Authorization Policies
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "RequireAdminRole");
    options.Conventions.AuthorizeFolder("/LGU", "RequireLGURole");
    options.Conventions.AuthorizeFolder("/User", "RequireUserRole");
    // Allow anonymous access to the login pages
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/LGU/Login");
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireLGURole", policy => policy.RequireRole("LGU"));
    options.AddPolicy("RequireUserRole", policy => policy.RequireRole("User"));
});

// 5. Register Face and ID Verification Service ← ADDED
builder.Services.AddHttpClient();
builder.Services.AddScoped<FaceVerificationService>();
builder.Services.AddScoped<AwsFaceVerificationService>();
builder.Services.AddScoped<IdValidationService>();

// 5a. Realtime feed (SignalR) — pushes new concerns/posts/ratings to connected
// clients so the Discover feed and LGU dashboard update without a page refresh.
builder.Services.AddSignalR();

// 5b. Background purge of sensitive ID/selfie images (Data Privacy Act retention).
builder.Services.AddSingleton<SensitiveMediaRetentionService>();
builder.Services.AddHostedService(services =>
    services.GetRequiredService<SensitiveMediaRetentionService>());
builder.Services.AddHostedService<IdentityMediaCloudBackupService>();
builder.Services.AddHostedService<RejectedApplicationPurgeService>();

// 5c. Location Density Score for the Urgency Algorithm (PostGIS-backed).
builder.Services.AddScoped<UrgencyScoreService>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maximumUploadRequestSize;
});

// Render terminates TLS at its reverse proxy and forwards the original request
// scheme and client address. Trust only the nearest proxy hop so HTTPS-dependent
// middleware (HSTS, redirects, secure links) sees the public request correctly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    // Render's proxy addresses are dynamic. The application port is not exposed
    // directly in production, so the immediate connection is always Render's proxy.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

var app = builder.Build();

// 6. HTTP Pipeline Configuration
app.UseForwardedHeaders();

// Development remains report-only so Visual Studio Browser Link and hot reload
// keep working. Deployed environments enforce the tested policy.
const string cspPolicy =
    "default-src 'self'; " +
    "base-uri 'self'; " +
    "object-src 'none'; " +
    "frame-ancestors 'none'; " +
    "form-action 'self'; " +
    "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' https://cdn.jsdelivr.net https://code.jquery.com https://www.google.com https://www.gstatic.com https://maps.googleapis.com; " +
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://www.gstatic.com; " +
    "font-src 'self' data: https://fonts.gstatic.com; " +
    "img-src 'self' data: blob: https://res.cloudinary.com https://maps.google.com https://maps.gstatic.com https://maps.googleapis.com https://*.googleusercontent.com; " +
    "connect-src 'self' https://*.amazonaws.com wss://*.amazonaws.com https://*.google.com https://*.googleapis.com wss:; " +
    "frame-src https://*.google.com; " +
    "media-src 'self' blob: https://res.cloudinary.com; " +
    "worker-src 'self' blob:;";

// Apply baseline browser security headers to every response, including static
// files, redirects, and error pages.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(self), geolocation=(self), microphone=()";
        var cspHeaderName = app.Environment.IsDevelopment()
            ? "Content-Security-Policy-Report-Only"
            : "Content-Security-Policy";
        context.Response.Headers[cspHeaderName] = cspPolicy;
        return Task.CompletedTask;
    });

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/json"
});

// Preserve old concern links, but make the browser use the new canonical URL.
// Status 307 also preserves the HTTP method for any older toggle/read requests.
app.Use(async (context, next) =>
{
    if (string.Equals(
        context.Request.Path.Value,
        "/User/Notifications",
        StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
        context.Response.Headers.Location = "/User/Concerns" + context.Request.QueryString;
        return;
    }

    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<FeedHub>("/hubs/feed");

// Unauthenticated, DB-free liveness probe — pinged periodically by an
// external uptime monitor to stop the free Render instance from spinning
// down on idle.
app.MapMethods("/health", new[] { HttpMethods.Get, HttpMethods.Head }, () => Results.Ok("healthy")).AllowAnonymous();

// 7. Role Seeding Logic
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var roles = new[] { "Admin", "LGU", "User" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Seed the administrator only when credentials are supplied through secure
    // configuration (for Render: SeedAdmin__Email and SeedAdmin__Password).
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var adminEmail = app.Configuration["SeedAdmin:Email"];
    var adminPassword = app.Configuration["SeedAdmin:Password"];

    if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
    {
        app.Logger.LogInformation(
            "Administrator seeding skipped because SeedAdmin credentials are not configured.");
    }
    else
    {
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                EmployeeId = "ADMIN-001",
                ApprovalStatus = "Approved",
                CreatedAt = DateTime.UtcNow
            };
            var adminResult = await userManager.CreateAsync(adminUser, adminPassword);
            if (adminResult.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
            else
            {
                app.Logger.LogError(
                    "Administrator seeding failed: {Errors}",
                    string.Join("; ", adminResult.Errors.Select(error => error.Description)));
            }
        }
    }
    // Clear leftover LGU fields from plain User accounts
    var usersWithFields = userManager.Users
        .Where(u => u.Department != null || u.EmployeeId != null)
        .ToList();
    foreach (var u in usersWithFields)
    {
        var isLgu = await userManager.IsInRoleAsync(u, "LGU");
        var isAdmin = await userManager.IsInRoleAsync(u, "Admin");
        if (!isLgu && !isAdmin)
        {
            u.Department = null;
            u.EmployeeId = null;
            await userManager.UpdateAsync(u);
        }
    }

    // Seed LGU accounts only when a password is supplied through secure
    // configuration (for Render: SeedAccounts__LguPassword).
    var lguSeedPassword = app.Configuration["SeedAccounts:LguPassword"];
    var lguAccounts = new[]
    {
        new { Email = "mikaellagomez102004@gmail.com",   EmployeeId = "LGU-EXT-001",   Department = "SWDO" },
        new { Email = "adrndgaming@gmail.com",           EmployeeId = "LGU-EXT-002",   Department = "CEO" },
        new { Email = "carlostannnn29+lgu@gmail.com",    EmployeeId = "LGU-EXT-003",   Department = "ACDO" },
        new { Email = "alcuizargiogio+lgu@gmail.com",    EmployeeId = "LGU-ENV-001",   Department = "CENRO" },
        new { Email = "ptro@voxangelos.gov.ph",          EmployeeId = "LGU-PTR-001",   Department = "PTRO" },
        new { Email = "osca@voxangelos.gov.ph",          EmployeeId = "LGU-OSCA-001",  Department = "OSCA" },
        new { Email = "pwdao@voxangelos.gov.ph",         EmployeeId = "LGU-PWDAO-001", Department = "PWDAO" },
    };

    if (string.IsNullOrWhiteSpace(lguSeedPassword))
    {
        app.Logger.LogInformation(
            "LGU account seeding skipped because SeedAccounts:LguPassword is not configured.");
    }
    else
    {
        foreach (var lgu in lguAccounts)
        {
        var existingLgu = await userManager.FindByEmailAsync(lgu.Email);
        if (existingLgu != null)
            continue;

        // Each department can only have one LGU account (unique DB index) — if another
        // account already covers this department, skip rather than crash the seeder.
        var departmentTaken = userManager.Users.Any(u => u.Department == lgu.Department);
        if (departmentTaken)
            continue;

        var lguUser = new ApplicationUser
        {
            UserName = lgu.Email,
            Email = lgu.Email,
            EmailConfirmed = true,
            EmployeeId = lgu.EmployeeId,
            Department = lgu.Department,
            ApprovalStatus = "Approved",
            CreatedAt = DateTime.UtcNow
        };
        var lguResult = await userManager.CreateAsync(lguUser, lguSeedPassword);
        if (lguResult.Succeeded)
            await userManager.AddToRoleAsync(lguUser, "LGU");
        }
    }

    // Seed test citizen accounts only when a password is supplied through
    // secure configuration (for Render: SeedAccounts__CitizenPassword).
    var citizenSeedPassword = app.Configuration["SeedAccounts:CitizenPassword"];
    var citizenAccounts = new[]
    {
    new { Email = "juan@gmail.com", FirstName = "Juan", MiddleName = "Santos", LastName = "Dela Cruz", Barangay = "Sto. Rosario", City = "Angeles City" },
    new { Email = "maria@gmail.com", FirstName = "Maria", MiddleName = "Reyes", LastName = "Santos", Barangay = "Balibago", City = "Angeles City" },
};

    if (string.IsNullOrWhiteSpace(citizenSeedPassword))
    {
        app.Logger.LogInformation(
            "Citizen account seeding skipped because SeedAccounts:CitizenPassword is not configured.");
    }
    else
    {
        foreach (var citizen in citizenAccounts)
        {
        var existing = await userManager.FindByEmailAsync(citizen.Email);
        if (existing == null)
        {
            var citizenUser = new ApplicationUser
            {
                UserName = citizen.Email,
                Email = citizen.Email,
                EmailConfirmed = true,
                ApprovalStatus = "Approved",
                CreatedAt = DateTime.UtcNow
            };
            var citizenResult = await userManager.CreateAsync(citizenUser, citizenSeedPassword);
            if (citizenResult.Succeeded)
            {
                await userManager.AddToRoleAsync(citizenUser, "User");

                // Create matching UserProfile
                dbContext.UserProfiles.Add(new UserProfile
                {
                    UserId = citizenUser.Id,
                    FirstName = citizen.FirstName,
                    MiddleName = citizen.MiddleName,
                    LastName = citizen.LastName,
                    Barangay = citizen.Barangay,
                    City = citizen.City
                });

            }
        }
        }
    }
    await dbContext.SaveChangesAsync();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
