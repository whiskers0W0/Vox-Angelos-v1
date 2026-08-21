namespace VoxAngelos.IntegrationTests.TestSupport;

/// <summary>
/// Points the integration-test suite at the local Postgres+PostGIS instance and the
/// app instance launched with Testing:BypassRecaptcha / Testing:SuppressExternalNotifications
/// set — never the shared cloud (Neon) database used by the rest of the team.
/// </summary>
public static class TestConfig
{
    public const string BaseUrl = "https://localhost:7244";

    public const string LocalConnectionString =
        "Host=localhost;Port=5433;Database=voxangelos_test;Username=postgres;Password=voxtest123;SSL Mode=Disable";

    // Seeded by Program.cs on every startup — see VoxAngelos/Program.cs role-seeding block.
    public const string AdminEmail = "carlostannnn29@gmail.com";
    public const string AdminPassword = "Admin@123456";

    // SWDO department LGU account.
    public const string LguEmail = "mikaellagomez102004@gmail.com";
    public const string LguPassword = "Lgu@123456";
    public const string LguDepartment = "SWDO";

    // Seeded citizen accounts (no phone number on file, so login never triggers SMS).
    public const string CitizenEmail = "juan@gmail.com";
    public const string CitizenPassword = "Citizen@123456";
    public const string SecondCitizenEmail = "maria@gmail.com";
    public const string SecondCitizenPassword = "Citizen@123456";

    // A validated point inside the Angeles City geofence (wwwroot/geojson/angeles-city-barangays.geojson).
    public const double InsideAngelesLatitude = 15.1455;
    public const double InsideAngelesLongitude = 120.5887;

    // Manila — well outside the Angeles City boundary.
    public const double OutsideAngelesLatitude = 14.5995;
    public const double OutsideAngelesLongitude = 120.9842;
}
