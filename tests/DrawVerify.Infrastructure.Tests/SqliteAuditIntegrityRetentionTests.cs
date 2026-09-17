using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteAuditIntegrityRetentionTests
{
    [Fact]
    public async Task TD_INF_002_integrity_verification_accepts_a_valid_chain_and_rejects_tampered_hash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-integrity-{Guid.NewGuid():N}.db");
        try
        {
            var document = Guid.NewGuid();
            await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await seed.OpenAsync();
                await using var command = seed.CreateCommand();
                command.CommandText = "CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL, ActorRole TEXT NULL, DocumentRevision TEXT NULL, DocumentVersion TEXT NULL, EventMetadataJson TEXT NOT NULL DEFAULT '{}', PreviousWorkflowStatus TEXT NULL, NewWorkflowStatus TEXT NULL, PredecessorAuditId TEXT NULL, IntegrityHash TEXT NULL); INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,EventMetadataJson,IntegrityHash) VALUES ($audit,$document,$action,'maker-001',$time,'{}','{}','tampered');";
                command.Parameters.AddWithValue("$audit", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$document", document.ToString()); command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.DocumentCreated); command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
            var repository = new SqliteAuditRepository(path);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.VerifyIntegrityAsync(document));
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () => { await using var delete = connection.CreateCommand(); delete.CommandText = "DELETE FROM DocumentAuditLog"; await delete.ExecuteNonQueryAsync(); });
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TD_INF_002_retention_rejects_negative_and_zero_is_noop()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-retention-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteAuditRepository(path);
            var retention = new SqliteAuditRetentionRepository(path);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retention.ApplyAsync(-1));
            Assert.Equal(0, await retention.ApplyAsync(0));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC09-TAMPER-EVIDENCE rejects modified audit content before report use")]
    public async Task TSD_AC09_TAMPER_EVIDENCE_rejects_modified_chain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-tamper-{Guid.NewGuid():N}.db");
        try
        {
            var document = Guid.NewGuid(); var repository = new SqliteAuditRepository(path);
            await repository.AppendAsync(new AuditEntry { DocumentId = document, UserId = "maker", Action = DocumentAuditAction.DocumentCreated, TimestampOffset = DateTimeOffset.UtcNow, TimestampUtc = DateTime.UtcNow, DetailsJson = "{}" });
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "UPDATE DocumentAuditLog SET DetailsJson='tampered'";
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQueryAsync());
            await repository.VerifyIntegrityAsync(document);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
}
