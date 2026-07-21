using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using VoxAngelos.Data;
using VoxAngelos.Hubs;
using VoxAngelos.Services; // ← ADDED

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var rawUrl = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

string connectionString;
if (rawUrl.StartsWith("postgresql://") || rawUrl.StartsWith("postgres://"))
{
    var uri = new Uri(rawUrl);
    var userInfo = uri.UserInfo.Split(':');
    connectionString = $"Host={uri.Host};Port={(uri.Port == -1 ? 5432 : uri.Port)};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
}
else
{
    connectionString = rawUrl;
}

builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, VoxAngelos.Services.EmailSender>();

builder.Services.AddScoped<OcrService>();
builder.Services.AddScoped<ConcernClassificationService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RecommendationRatingService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite()));

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
builder.Services.AddScoped<IdValidationService>();

// 5a. Realtime feed (SignalR) — pushes new concerns/posts/ratings to connected
// clients so the Discover feed and LGU dashboard update without a page refresh.
builder.Services.AddSignalR();

// 5b. Background purge of sensitive ID/selfie images (Data Privacy Act retention).
builder.Services.AddHostedService<SensitiveMediaRetentionService>();

// 5c. Location Density Score for the Urgency Algorithm (PostGIS-backed).
builder.Services.AddScoped<UrgencyScoreService>();

// 5d. Rate limiting on the endpoints that call paid/quota-limited external APIs
// (Google Cloud NLP/Vision and the Hugging-Face-hosted face/ID verification API) —
// mitigates "Denial of Wallet" bot abuse of registration and concern submission.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Registration's identity-verification step (OCR + face match) is the most
    // expensive call chain in the app — it hits both Google Cloud Vision and the
    // Hugging Face face-verification API for a single request.
    options.AddPolicy("registration", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        }));

    // Concern/recommendation submission triggers a Google Cloud Natural Language
    // classification call per submission.
    options.AddPolicy("concern-submission", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity!.Name!
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"error\":\"Too many requests. Please wait a few minutes before trying again.\"}",
            token);
    };
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

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHub<FeedHub>("/hubs/feed");

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
        new { Email = "pptro@voxangelos.gov.ph",         EmployeeId = "LGU-PPT-001",   Department = "PPTRO" },
        new { Email = "osca@voxangelos.gov.ph",          EmployeeId = "LGU-OSCA-001",  Department = "OSCA" },
        new { Email = "pwdao@voxangelos.gov.ph",         EmployeeId = "LGU-PWDAO-001", Department = "PWDAO" },
    };

    foreach (var lgu in lguAccounts)
    {
        var existingLgu = await userManager.FindByEmailAsync(lgu.Email);
        if (existingLgu != null)
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
