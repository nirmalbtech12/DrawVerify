using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateServiceTests
{
    [Fact(DisplayName = "TSD-AC02 lifecycle creates, duplicates, versions, defaults, and restores templates")]
    public async Task Lifecycle_preserves_immutable_versions_and_default()
    {
        var path = TempDb();
        try
        {
            var auth = Admin();
            var service = new ReportTemplateService(new SqliteReportTemplateRepository(path), auth, new RoleAuthorization());
            var first = await service.CreateAsync("Primary", "admin");
            var duplicate = await service.DuplicateAsync(first.Id, "Copy", "admin");
            var revised = await service.SaveAsync(first with { Header = new() { OrganizationName = "Acme" } }, first.Version, "admin");
            await service.SetDefaultAsync(revised.Id, "admin");
            var restored = await service.RestoreAsync(revised.Id, 1, "admin");
            var versions = await service.GetVersionsAsync(revised.Id);

            Assert.NotEqual(first.Id, duplicate.Id);
            Assert.Equal("Acme", revised.Header.OrganizationName);
            Assert.True(restored.Version > revised.Version);
            Assert.Equal(new[] { 1, 2, 3, 4 }, versions.Select(x => x.Version));
            Assert.Single((await service.ListAsync()).Where(x => x.IsOrganizationDefault));
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TSD-AC02 invalid names and stale versions fail without mutation")]
    public async Task Invalid_and_stale_operations_are_explicit()
    {
        var path = TempDb();
        try
        {
            var service = new ReportTemplateService(new SqliteReportTemplateRepository(path), Admin(), new RoleAuthorization());
            var template = await service.CreateAsync("Valid", "admin");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(template with { Name = " " }, template.Version, "admin"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(template with { Name = "changed" }, 99, "admin"));
            Assert.Equal("Valid", (await service.ListAsync()).Single().Name);
        }
        finally { Cleanup(path); }
    }

    private static SessionAuthenticationService Admin() { var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin })); return auth; }
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"drawverify-template-{Guid.NewGuid():N}.db");
    private static void Cleanup(string path) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}
