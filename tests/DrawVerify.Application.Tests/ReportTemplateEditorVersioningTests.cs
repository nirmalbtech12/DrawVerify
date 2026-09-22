using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateEditorVersioningTests
{
    [Fact(DisplayName = "TSD-07 editor template versions remain independently readable")]
    public async Task Successive_versions_and_stale_save_are_visible()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-editor-version-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteReportTemplateRepository(path); var id = Guid.NewGuid();
            var first = await repository.SaveAsync(new ReportTemplate { Id = id, Name = "Initial" }, 0, Audit(id));
            var second = await repository.SaveAsync(first with { Name = "Edited" }, first.Version, Audit(id));
            Assert.Equal(2, second.Version);
            Assert.Equal("Initial", (await repository.GetAsync(id, 1))!.Name);
            Assert.Equal("Edited", (await repository.GetAsync(id, 2))!.Name);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(second with { Name = "Stale" }, 1, Audit(id)));
            Assert.Equal("Edited", (await repository.GetAsync(id))!.Name);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }

    private static ReportTemplateAuditEntry Audit(Guid id) => new() { TemplateId = id, ActorId = "tester", Action = "Modified", Summary = "template" };
}
