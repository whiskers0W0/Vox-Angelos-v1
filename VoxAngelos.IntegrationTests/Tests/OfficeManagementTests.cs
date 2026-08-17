using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxAngelos.Data;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.Tests;

/// <summary>
/// IT-05 — Admin/OfficeManagement page -> Identity (UserManager.CreateAsync + role
/// assignment) -> ApplicationDbContext unique-department validation.
/// </summary>
[Collection("VoxAngelos App")]
public class OfficeManagementTests(IdentityTestServices identity)
{
    [Fact]
    public async Task IT05_AdminProvisionsLguAccount_AndBlocksDuplicateDepartment()
    {
        var admin = await LoginFlow.LoginAsync(identity, TestConfig.AdminEmail, TestConfig.AdminPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var department = $"TESTDEPT{suffix}".ToUpperInvariant();
        var email = $"it-office-{suffix}@example.test";

        var createResponse = await admin.Client.PostFormAsync(
            "/Admin/OfficeManagement",
            handler: "Create",
            fields: new Dictionary<string, string>
            {
                ["NewEmployeeId"] = $"EMP-{suffix}",
                ["NewEmail"] = email,
                ["NewDepartment"] = department,
                ["NewDepartmentFullName"] = "Integration Test Department",
                ["NewTags"] = "",
                ["NewCategories"] = ""
            });

        Assert.Equal(System.Net.HttpStatusCode.OK, createResponse.StatusCode);

        using (var scope = identity.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var created = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
            Assert.NotNull(created);
            Assert.Equal(department, created!.Department);
            Assert.Equal("Approved", created.ApprovalStatus);
        }

        // Duplicate department, different employee/email — must be rejected.
        var duplicateSuffix = Guid.NewGuid().ToString("N")[..8];
        var duplicateEmail = $"it-office-dup-{duplicateSuffix}@example.test";
        var duplicateResponse = await admin.Client.PostFormAsync(
            "/Admin/OfficeManagement",
            handler: "Create",
            fields: new Dictionary<string, string>
            {
                ["NewEmployeeId"] = $"EMP-{duplicateSuffix}",
                ["NewEmail"] = duplicateEmail,
                ["NewDepartment"] = department,
                ["NewDepartmentFullName"] = "Integration Test Department Duplicate",
                ["NewTags"] = "",
                ["NewCategories"] = ""
            });

        Assert.Equal(System.Net.HttpStatusCode.OK, duplicateResponse.StatusCode);
        var duplicateBody = await duplicateResponse.Content.ReadAsStringAsync();
        Assert.Contains("already exists", duplicateBody, StringComparison.OrdinalIgnoreCase);

        using (var scope = identity.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var duplicateCreated = await db.Users.SingleOrDefaultAsync(u => u.Email == duplicateEmail);
            Assert.Null(duplicateCreated);
        }
    }
}
