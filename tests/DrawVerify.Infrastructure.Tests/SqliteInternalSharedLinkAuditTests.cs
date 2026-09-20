using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalSharedLinkAuditTests
{
    [Fact(DisplayName = "TSD-AC11 SQLite audit stores exact shared-link access action and trace fields")]
    public async Task Audit_repository_round_trips_shared_link_access()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAuditRepository(path);
            var document = Guid.NewGuid();
            await repository.AppendAsync(new AuditEntry { DocumentId = document, UserId = "viewer-1", DocumentVersion = "version-1", Action = DocumentAuditAction.AccessedViaSharedLink, TimestampUtc = DateTime.UtcNow, TimestampOffset = DateTimeOffset.UtcNow, DetailsJson = "{\"linkId\":\"link-1\",\"action\":\"AccessedViaSharedLink\"}" });
            var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document)));
            Assert.Equal(DocumentAuditAction.AccessedViaSharedLink, record.Action);
            Assert.Equal("version-1", record.DocumentVersion);
            Assert.Equal("viewer-1", record.ActorId);
            Assert.Contains("link-1", record.DetailsJson, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
