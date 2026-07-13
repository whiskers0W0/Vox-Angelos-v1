// One-off tool: seeds docs/test-data/concern_test_cases_500.csv into the live
// Concerns table, attributed to a dedicated "nlp-test-dataset" citizen account, running
// each row through the real ConcernClassificationService (Google NLP + keyword
// fallback) so LGU staff can verify actual classifier output against the dataset's
// expected_category column. See docs/test-data/README.md for the verification workflow.
//
// Usage:
//   dotnet run -- <connectionStringUrl> <googleCredentialsJsonPath> <csvPath>
//
// Never commit the connection string or credentials path — pass them as CLI args only.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoxAngelos.Data;
using VoxAngelos.Services;

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run -- <connectionStringUrl> <googleCredentialsJsonPath> <csvPath>");
    Console.WriteLine("   or: dotnet run -- --count <connectionStringUrl>   (checks how many test rows are already seeded)");
    return 1;
}

if (args[0] == "--count" || args[0] == "--report")
{
    var countConn = ResolveConnectionString(args[1]);
    var countServices = new ServiceCollection();
    countServices.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(countConn, n => n.UseNetTopologySuite()));
    await using var countProvider = countServices.BuildServiceProvider();
    using var countScope = countProvider.CreateScope();
    var countDb = countScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var seeded = await countDb.Concerns
        .Where(c => c.LguNotes != null && c.LguNotes.StartsWith("[NLP TEST DATASET]"))
        .Select(c => new { c.Category, c.LguNotes })
        .ToListAsync();
    Console.WriteLine($"Seeded test-dataset concerns currently in DB: {seeded.Count}");

    if (args[0] == "--report")
    {
        int m = 0;
        var total = new Dictionary<string, int>();
        var match = new Dictionary<string, int>();
        foreach (var c in seeded)
        {
            var idx = c.LguNotes!.IndexOf("expected_category=", StringComparison.Ordinal);
            if (idx < 0) continue;
            var expected = c.LguNotes.Substring(idx + "expected_category=".Length).Split(';')[0].Trim();
            total[expected] = total.GetValueOrDefault(expected) + 1;
            if (c.Category == expected) { m++; match[expected] = match.GetValueOrDefault(expected) + 1; }
        }
        Console.WriteLine($"Overall live-classifier match rate vs expected_category: {m}/{seeded.Count} ({m * 100.0 / seeded.Count:0.0}%)");
        foreach (var dept in total.Keys.OrderBy(k => k))
        {
            var t = total[dept];
            var mm = match.GetValueOrDefault(dept);
            Console.WriteLine($"  {dept}: {mm}/{t} ({mm * 100.0 / t:0.0}%)");
        }
    }
    return 0;
}

if (args.Length < 3)
{
    Console.WriteLine("Usage: dotnet run -- <connectionStringUrl> <googleCredentialsJsonPath> <csvPath>");
    return 1;
}

var rawUrl = args[0];
var credentialsPath = args[1];
var csvPath = args[2];

const string TestAccountEmail = "nlp-test-dataset@voxangelos.gov.ph";
const string TestAccountNoteTag = "[NLP TEST DATASET]";

var connectionString = ResolveConnectionString(rawUrl);

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite()));
services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher<ApplicationUser>>();
services.AddScoped<ConcernClassificationService>();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var sp = scope.ServiceProvider;

var db = sp.GetRequiredService<ApplicationDbContext>();
var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
var classifier = sp.GetRequiredService<ConcernClassificationService>();

Console.WriteLine("Connecting to database and verifying test citizen account...");

var testUser = await userManager.FindByEmailAsync(TestAccountEmail);
if (testUser == null)
{
    testUser = new ApplicationUser
    {
        UserName = TestAccountEmail,
        Email = TestAccountEmail,
        EmailConfirmed = true,
        ApprovalStatus = "Approved",
        CreatedAt = DateTime.UtcNow
    };
    var password = Guid.NewGuid().ToString("N") + "Aa1!";
    var result = await userManager.CreateAsync(testUser, password);
    if (!result.Succeeded)
    {
        Console.WriteLine("Failed to create test account: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return 1;
    }
    await userManager.AddToRoleAsync(testUser, "User");

    db.UserProfiles.Add(new UserProfile
    {
        UserId = testUser.Id,
        FirstName = "NLP",
        LastName = "Test Dataset",
    });
    await db.SaveChangesAsync();
    Console.WriteLine($"Created test citizen account: {TestAccountEmail}");
}
else
{
    Console.WriteLine($"Using existing test citizen account: {TestAccountEmail}");
}

var allRows = ParseCsv(csvPath);
Console.WriteLine($"Loaded {allRows.Count} rows from {Path.GetFileName(csvPath)}.");

// Resume support: skip rows whose exact text was already inserted for the test
// account in a previous (possibly interrupted) run, so re-running this tool after a
// timeout/crash doesn't create duplicate concerns.
var alreadySeeded = (await db.Concerns
    .Where(c => c.CitizenId == testUser.Id)
    .Select(c => c.Description)
    .ToListAsync())
    .ToHashSet();
var rows = allRows.Where(r => !alreadySeeded.Contains(r.Text)).ToList();
Console.WriteLine($"{alreadySeeded.Count} already seeded, {rows.Count} remaining to insert.");

int inserted = 0, matched = 0;
var perDeptTotal = new Dictionary<string, int>();
var perDeptMatched = new Dictionary<string, int>();

foreach (var row in rows)
{
    var liveCategory = await classifier.ClassifyAsync(row.Text, credentialsPath);

    var concern = new Concern
    {
        CitizenId = testUser.Id,
        Description = row.Text,
        Category = liveCategory,
        Status = "Unresolved",
        LguNotes = $"{TestAccountNoteTag} lang={row.Language}; expected_category={row.ExpectedCategory}; " +
                   $"expected_urgency={row.ExpectedUrgency}. For classifier verification only — " +
                   "see docs/test-data/README.md. Do not action as a real complaint.",
        SubmittedAt = DateTime.UtcNow
    };
    db.Concerns.Add(concern);
    inserted++;

    perDeptTotal[row.ExpectedCategory] = perDeptTotal.GetValueOrDefault(row.ExpectedCategory) + 1;
    if (liveCategory == row.ExpectedCategory)
    {
        matched++;
        perDeptMatched[row.ExpectedCategory] = perDeptMatched.GetValueOrDefault(row.ExpectedCategory) + 1;
    }

    if (inserted % 10 == 0)
    {
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine($"  ...{inserted}/{rows.Count} inserted (running match rate: {matched * 100.0 / inserted:0.0}%)");
    }
}
await db.SaveChangesAsync();

Console.WriteLine();
Console.WriteLine($"Done. Inserted {inserted} concerns under {TestAccountEmail}.");
Console.WriteLine($"Overall live-classifier match rate vs expected_category: {matched}/{inserted} ({matched * 100.0 / inserted:0.0}%)");
Console.WriteLine("Per-department:");
foreach (var dept in perDeptTotal.Keys.OrderBy(k => k))
{
    var total = perDeptTotal[dept];
    var m = perDeptMatched.GetValueOrDefault(dept);
    Console.WriteLine($"  {dept}: {m}/{total} ({m * 100.0 / total:0.0}%)");
}
Console.WriteLine();
Console.WriteLine("Note: this is a raw match rate against this CSV's expected_category, not a substitute");
Console.WriteLine("for LGU human verification — see docs/test-data/README.md before trusting these numbers.");

return 0;

static string ResolveConnectionString(string rawUrl)
{
    if (rawUrl.StartsWith("postgresql://") || rawUrl.StartsWith("postgres://"))
    {
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        return $"Host={uri.Host};Port={(uri.Port == -1 ? 5432 : uri.Port)};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
    }
    return rawUrl;
}

static List<(string Text, string Language, string ExpectedCategory, string ExpectedUrgency)> ParseCsv(string path)
{
    var result = new List<(string, string, string, string)>();
    using var reader = new StreamReader(path);
    var header = true;
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (header) { header = false; continue; }
        if (string.IsNullOrWhiteSpace(line)) continue;

        var fields = SplitCsvLine(line);
        // id, text, language, expected_category, expected_urgency, lgu_verified, notes
        result.Add((fields[1], fields[2], fields[3], fields[4]));
    }
    return result;
}

static List<string> SplitCsvLine(string line)
{
    var fields = new List<string>();
    var current = new System.Text.StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = false;
            }
            else current.Append(c);
        }
        else
        {
            if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
    }
    fields.Add(current.ToString());
    return fields;
}
