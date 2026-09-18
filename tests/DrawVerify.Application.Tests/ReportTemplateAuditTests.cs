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
            await service.SetDefaultAsync(template.Id, "admin");
            var assigned = await service.CreateAsync("Assigned", "admin");
            await service.AssignAsync(new ReportTemplateAssignment { TemplateId = assigned.Id, ProjectId = "audit-project" }, "admin");

            var entries = await repository.GetAuditAsync(template.Id);
            Assert.Equal(new[] { "Created", "Modified", "DefaultChanged" }, entries.Select(entry => entry.Action));
            Assert.Equal(new[] { 1, 2, 3 }, entries.Select(entry => entry.Version));
            AssertAuditFields(entries, template.Id);
            Assert.Contains("Audited", entries[0].Summary);

            var assignmentEntries = await repository.GetAuditAsync(assigned.Id);
            var assignedEntry = Assert.Single(assignmentEntries.Where(entry => entry.Action == "Assigned"));
            Assert.Equal(assigned.Id, assignedEntry.TemplateId);
            Assert.Equal("admin", assignedEntry.ActorId);
            Assert.NotEqual(default, assignedEntry.TimestampUtc);
            Assert.Equal("Assigned", assignedEntry.Action);
            Assert.Equal("Project=audit-project; Workflow=", assignedEntry.Summary);
            Assert.True(assignedEntry.Version > 0);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static void AssertAuditFields(IReadOnlyList<ReportTemplateAuditEntry> entries, Guid templateId)
    {
        Assert.All(entries, entry =>
        {
            Assert.NotEqual(Guid.Empty, entry.AuditId);
            Assert.Equal(templateId, entry.TemplateId);
            Assert.Equal("admin", entry.ActorId);
            Assert.NotEqual(default, entry.TimestampUtc);
            Assert.False(string.IsNullOrWhiteSpace(entry.Action));
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
            Assert.True(entry.Version > 0);
        });
    }
}
