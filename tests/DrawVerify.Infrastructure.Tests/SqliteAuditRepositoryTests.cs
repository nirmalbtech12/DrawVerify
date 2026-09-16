using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteAuditRepositoryTests
{
    [Fact(DisplayName = "TD-04 persists Match, Mismatch, and Error verification events with chain integrity")]
    public async Task Verification_outcomes_are_queryable_and_integrity_checked()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-verification-{Guid.NewGuid():N}.db");
        try
        {
            var documentId = Guid.NewGuid();
            var repository = new SqliteAuditRepository(path);
            foreach (var result in new[] { "Match", "Mismatch", "Error" })
                await repository.WriteAsync(new AuditEntry { DocumentId = documentId, UserId = "checker-001", ActorRole = "Checker", DocumentRevision = "2", DocumentVersion = "step-2", Action = result == "Mismatch" ? DocumentAuditAction.IntegrityCheckFailed : result == "Error" ? DocumentAuditAction.VerificationRunFailed : DocumentAuditAction.VerificationRunCompleted, TimestampUtc = DateTime.UtcNow, DetailsJson = $"{{\"result\":\"{result}\",\"storedFingerprint\":\"aaa\",\"recomputedFingerprint\":\"bbb\"}}" });
            var rows = await repository.QueryAsync(new AuditLogQuery(DocumentId: documentId));
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, row => row.DetailsJson.Contains("\"result\":\"Match\"", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.DetailsJson.Contains("\"result\":\"Mismatch\"", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.DetailsJson.Contains("\"result\":\"Error\"", StringComparison.Ordinal));
            await repository.VerifyIntegrityAsync(documentId);
        }
        finally { Cleanup(path); }
    }
    [Fact(DisplayName = "TD-08 preserves fingerprint fields in the submission audit event")]
    public async Task Submission_audit_round_trips_fingerprint_context_and_integrity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-fingerprint-{Guid.NewGuid():N}.db");
        try
        {
            var documentId = Guid.NewGuid();
            var fingerprint = new string('c', 64);
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry
            {
                DocumentId = documentId, UserId = "maker-001", ActorRole = "Maker", DocumentRevision = "3",
                Action = DocumentAuditAction.SubmissionSucceeded, TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DetailsJson = $"{{\"fingerprint\":\"{fingerprint}\",\"fingerprintAlgorithm\":\"SHA-256\",\"fingerprintCanonicalizationVersion\":1}}"
            });

            var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: documentId)));
            Assert.Contains(fingerprint, record.DetailsJson);
            Assert.Contains("fingerprintAlgorithm", record.DetailsJson);
            await repository.VerifyIntegrityAsync(documentId);
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-05 audit entries are append-only")]
    public async Task Audit_update_and_delete_are_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-immutable-{Guid.NewGuid():N}.db");
        try
        {
            var documentId = Guid.NewGuid();
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry { DocumentId = documentId, UserId = "maker-001", Action = DocumentAuditAction.DocumentCreated, TimestampUtc = DateTime.UtcNow, DetailsJson = "{}" });
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            foreach (var sql in new[] { "UPDATE DocumentAuditLog SET DetailsJson='{}' WHERE DocumentId='" + documentId + "'", "DELETE FROM DocumentAuditLog WHERE DocumentId='" + documentId + "'" })
            {
                await using var command = connection.CreateCommand(); command.CommandText = sql;
                await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            }
        }
        finally { Cleanup(path); }
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
