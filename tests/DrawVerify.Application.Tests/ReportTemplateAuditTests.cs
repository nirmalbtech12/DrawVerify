using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateAuditTests
{
    [Fact(DisplayName = "TSD-AC14 successful mutations create complete template audit records")]
    public async Task Audit_records_include_actor_version_action_and_summary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-{Guid.NewGuid():N}.db");
        try
        {
            var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var repository = new SqliteReportTemplateRepository(path);
            var service = new ReportTemplateService(repository, auth, new RoleAuthorization());
            var template = await service.CreateAsync("Audited", "admin");
            await service.SaveAsync(template with { Name = "Audited v2" }, template.Version, "admin");
            var entries = await repository.GetAuditAsync(template.Id);
            Assert.Equal(2, entries.Count); Assert.Equal("admin", entries[0].ActorId); Assert.Equal("Created", entries[0].Action); Assert.Contains("Audited", entries[0].Summary);
            Assert.Equal("Modified", entries[1].Action); Assert.All(entries, entry => Assert.NotEqual(Guid.Empty, entry.AuditId));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
