using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxAngelos.Data;
using VoxAngelos.Services; // ← ADDED

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, VoxAngelos.Services.EmailSender>();

builder.Services.AddScoped<OcrService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

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
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

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
    var adminEmail = "admin@voxangelos.gov.ph";
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

    // Seed LGU accounts
    var lguAccounts = new[]
    {
    new { Email = "health@voxangelos.gov.ph",     EmployeeId = "LGU-HLT-001", Department = "Health Office" },
    new { Email = "engineering@voxangelos.gov.ph", EmployeeId = "LGU-ENG-001", Department = "Engineering Office" },
    new { Email = "socialwelfare@voxangelos.gov.ph",EmployeeId = "LGU-SWD-001", Department = "Social Welfare" },
    new { Email = "publicsafety@voxangelos.gov.ph", EmployeeId = "LGU-PSF-001", Department = "Public Safety" },
    new { Email = "agriculture@voxangelos.gov.ph",  EmployeeId = "LGU-AGR-001", Department = "Agriculture" },
};

    foreach (var lgu in lguAccounts)
    {
        var existing = await userManager.FindByEmailAsync(lgu.Email);
        if (existing == null)
        {
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
            {
                await userManager.AddToRoleAsync(lguUser, "LGU");
            }
        }
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

app.Run();
