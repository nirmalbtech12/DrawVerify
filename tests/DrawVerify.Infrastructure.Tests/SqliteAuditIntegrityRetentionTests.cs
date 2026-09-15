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
            var repository = new SqliteAuditRepository(path);
            var document = Guid.NewGuid();
            await repository.AppendAsync(new AuditEntry { DocumentId = document, UserId = "maker-001", Action = DocumentAuditAction.DocumentCreated, TimestampUtc = DateTime.UtcNow, TimestampOffset = DateTimeOffset.UtcNow });
            await repository.VerifyIntegrityAsync(document);
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "UPDATE DocumentAuditLog SET IntegrityHash='tampered' WHERE DocumentId=$id"; command.Parameters.AddWithValue("$id", document.ToString()); await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.VerifyIntegrityAsync(document));
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
}
