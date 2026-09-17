using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteReportTemplateRepositoryTemplateTests
{
    [Fact(DisplayName = "TSD-AC02 TSD-AC10 SQLite stores immutable template versions")]
    public async Task Repository_persists_history_and_atomic_default()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-sqlite-template-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteReportTemplateRepository(path);
            var id = Guid.NewGuid();
            var first = await repository.SaveAsync(new ReportTemplate { Id = id, Name = "one" }, 0, Audit(id, "Created"));
            var second = await repository.SaveAsync(first with { Name = "two", IsOrganizationDefault = true }, first.Version, Audit(id, "Modified"));
            var history = await repository.GetVersionsAsync(id);
            Assert.Equal(new[] { 1, 2 }, history.Select(x => x.Version)); Assert.Equal("one", history[0].Snapshot.Name); Assert.Equal("two", (await repository.GetAsync(id))!.Name);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(second with { Name = "bad" }, 1, Audit(id, "Modified")));
            Assert.Equal("two", (await repository.GetAsync(id))!.Name);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC11 assignment resolution and TSD-AC14 audit records are persisted")]
    public async Task Assignment_and_audit_are_queryable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-sqlite-assignment-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteReportTemplateRepository(path); var template = new ReportTemplate { Id = Guid.NewGuid(), Name = "assigned" };
            await repository.SaveAsync(template, 0, Audit(template.Id, "Created"));
            await repository.AssignAsync(new ReportTemplateAssignment { TemplateId = template.Id, ProjectId = "P1" }, Audit(template.Id, "Assigned"));
            Assert.Equal(template.Id, (await repository.ResolveAsync("P1", null))!.Id);
            var audit = await repository.GetAuditAsync(template.Id); Assert.Equal(new[] { "Created", "Assigned" }, audit.Select(x => x.Action));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static ReportTemplateAuditEntry Audit(Guid id, string action) => new() { TemplateId = id, ActorId = "test-actor", Action = action, Summary = "test" };
}
