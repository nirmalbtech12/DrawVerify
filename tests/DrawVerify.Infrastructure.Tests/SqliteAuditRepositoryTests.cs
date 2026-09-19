using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteAuditRepositoryTests
{
    [Fact(DisplayName = "TSD-AC12-AUDIT preserves sharing action enum values")]
    public void Sharing_audit_actions_are_distinct()
    {
        Assert.NotEqual(DocumentAuditAction.AccessGranted, DocumentAuditAction.AccessModified);
        Assert.NotEqual(DocumentAuditAction.AccessModified, DocumentAuditAction.AccessRevoked);
    }

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

    [Fact(DisplayName = "TD-KAN70-07 VerificationReportGenerated audit is queryable by document and preserves scope")]
    public async Task Verification_report_event_is_queryable_with_complete_scope_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-report-{Guid.NewGuid():N}.db");
        var documentId = Guid.NewGuid();
        try
        {
            var repository = new SqliteAuditRepository(path);
            var entry = new AuditEntry
            {
                DocumentId = documentId, UserId = "checker-id", ActorRole = "Checker", DocumentRevision = "7", DocumentVersion = "model-3",
                Action = DocumentAuditAction.VerificationReportGenerated, TimestampUtc = DateTime.UtcNow,
                DetailsJson = "{\"verificationSessionId\":\"11111111-1111-1111-1111-111111111111\",\"reportType\":\"VerificationReport\",\"reportFormat\":\"PDF\",\"scope\":{\"gdtOnly\":true}}"
            };
            await repository.WriteAsync(entry);

            var result = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: documentId)));
            Assert.Equal(DocumentAuditAction.VerificationReportGenerated, result.Action);
            Assert.Equal("checker-id", result.ActorId);
            Assert.Equal("Checker", result.ActorRole);
            Assert.Equal("7", result.DocumentRevision);
            Assert.Equal("model-3", result.DocumentVersion);
            Assert.Contains("VerificationReport", result.DetailsJson);
            Assert.Contains("gdtOnly", result.DetailsJson);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
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

    [Fact(DisplayName = "TSD-AC08-ORDERING orders equal timestamps by AuditId and returns all rows")]
    public async Task TSD_AC08_ORDERING_uses_deterministic_timestamp_and_audit_id_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-order-{Guid.NewGuid():N}.db");
        try
        {
            var document = Guid.NewGuid(); var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"); var repository = new SqliteAuditRepository(path);
            foreach (var id in new[] { Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000001") }) await repository.AppendAsync(new AuditEntry { AuditId = id, DocumentId = document, UserId = "maker", Action = DocumentAuditAction.DocumentCreated, TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, DetailsJson = "{}" });
            var records = await repository.QueryAsync(new AuditLogQuery(DocumentId: document));
            Assert.Equal(new[] { Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") }, records.Select(record => record.AuditId));
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TSD-AC13-GENERATION-AUDIT stores report-generation metadata")]
    public async Task TSD_AC13_GENERATION_AUDIT_stores_complete_generation_event()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-generation-{Guid.NewGuid():N}.db");
        try
        {
            var document = Guid.NewGuid(); var repository = new SqliteAuditRepository(path); var timestamp = DateTimeOffset.Parse("2026-09-17T10:00:00Z");
            await repository.AppendAsync(new AuditEntry { DocumentId = document, UserId = "checker", ActorRole = "Checker", DocumentVersion = "v3", Action = DocumentAuditAction.VerificationReportGenerated, TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, DetailsJson = "{\"reportType\":\"VerificationReportWithAuditTrail\"}" });
            var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document)));
            Assert.Equal(DocumentAuditAction.VerificationReportGenerated, record.Action); Assert.Equal("checker", record.ActorId); Assert.Contains("VerificationReportWithAuditTrail", record.DetailsJson);
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
