using CloudinaryDotNet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services; 
using Microsoft.AspNetCore.Http.Features;

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

var app = builder.Build();

// 6. HTTP Pipeline Configuration
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

    // Seed Admin account
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var adminEmail = "carlostannnn29@gmail.com";
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
        var adminResult = await userManager.CreateAsync(adminUser, "Admin@123456");
        if (adminResult.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
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

    // Seed LGU accounts
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
        var lguResult = await userManager.CreateAsync(lguUser, "Lgu@123456");
        if (lguResult.Succeeded)
            await userManager.AddToRoleAsync(lguUser, "LGU");
    }

    // Seed Citizen accounts
    var citizenAccounts = new[]
    {
    new { Email = "juan@gmail.com", FirstName = "Juan", MiddleName = "Santos", LastName = "Dela Cruz", Barangay = "Sto. Rosario", City = "Angeles City" },
    new { Email = "maria@gmail.com", FirstName = "Maria", MiddleName = "Reyes", LastName = "Santos", Barangay = "Balibago", City = "Angeles City" },
};

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
            var citizenResult = await userManager.CreateAsync(citizenUser, "Citizen@123456");
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
    await dbContext.SaveChangesAsync();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
