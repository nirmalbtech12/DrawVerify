using DrawVerify.Core.Enums;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteDocumentAuditRepositoryTests
{
    [Fact(DisplayName = "TSD-AC02-LIFECYCLE-ACTIONS round-trips lifecycle actions with versions")]
    public async Task TSD_AC02_LIFECYCLE_ACTIONS_round_trips_action_and_version_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-lifecycle-{Guid.NewGuid():N}.db");
        try
        {
            var document = Guid.NewGuid(); var repository = new SqliteAuditRepository(path);
            await repository.AppendAsync(new AuditEntry { DocumentId = document, UserId = "maker", Action = DocumentAuditAction.FileUploaded, DocumentRevision = "REV-2", DocumentVersion = "VERSION-2", TimestampOffset = DateTimeOffset.UtcNow, TimestampUtc = DateTime.UtcNow, DetailsJson = "{}" });
            var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document)));
            Assert.Equal(DocumentAuditAction.FileUploaded, record.Action); Assert.Equal("REV-2", record.DocumentRevision); Assert.Equal("VERSION-2", record.DocumentVersion);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
    [Fact]
    public async Task TD_INF_001_round_trips_complete_history_and_isolates_documents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAuditRepository(path); var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"); var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            await repository.AppendAsync(Entry(document, DocumentAuditAction.DocumentCreated, "REV-001", new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.FromHours(5.5)))); await repository.AppendAsync(Entry(document, DocumentAuditAction.FileUploaded, "REV-001", new DateTimeOffset(2026, 1, 10, 8, 5, 0, TimeSpan.FromHours(5.5)), "{\"file\":\"part.step\"}")); await repository.AppendAsync(Entry(other, DocumentAuditAction.DocumentViewed, "REV-002", new DateTimeOffset(2026, 1, 10, 8, 10, 0, TimeSpan.Zero)));
            var records = await repository.QueryAsync(new AuditLogQuery(DocumentId: document)); Assert.Equal(2, records.Count); Assert.Equal(new[] { DocumentAuditAction.DocumentCreated, DocumentAuditAction.FileUploaded }, records.Select(x => x.Action)); Assert.Equal("maker-001", records[0].ActorId); Assert.Equal(TimeSpan.FromHours(5.5), records[0].Timestamp.Offset); Assert.Equal("REV-001", records[0].DocumentRevision); Assert.Contains("part.step", records[1].EventMetadataJson); await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.AppendAsync(new AuditEntry { AuditId = records[0].AuditId, DocumentId = document, UserId = "maker-001", Action = DocumentAuditAction.DocumentViewed, TimestampOffset = DateTimeOffset.UtcNow, TimestampUtc = DateTime.UtcNow }));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-03 persists complete PriorVersionViewed audit fields")]
    public async Task Prior_version_view_audit_round_trips_exact_access_context()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-prior-{Guid.NewGuid():N}.db");
        try { var repository = new SqliteAuditRepository(path); var document = Guid.NewGuid(); var when = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero); await repository.AppendAsync(new AuditEntry { AuditId = Guid.NewGuid(), DocumentId = document, UserId = "checker-001", ActorRole = "Checker", DocumentRevision = "7", DocumentVersion = "version-7", TimestampOffset = when, TimestampUtc = when.UtcDateTime, Action = DocumentAuditAction.PriorVersionViewed, DetailsJson = "{\"action\":\"PriorVersionViewed\",\"revision\":7}" }); var entry = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document))); Assert.Equal(DocumentAuditAction.PriorVersionViewed, entry.Action); Assert.Equal("checker-001", entry.ActorId); Assert.Equal("Checker", entry.ActorRole); Assert.Equal("7", entry.DocumentRevision); Assert.Equal("version-7", entry.DocumentVersion); Assert.Equal(when, entry.Timestamp); Assert.Contains("PriorVersionViewed", entry.DetailsJson); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-003 CheckerSignatureApplied audit stores complete document version context")]
    public async Task Checker_signature_audit_round_trips_complete_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-checker-{Guid.NewGuid():N}.db"); var document = Guid.NewGuid(); var when = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        try { var repository = new SqliteAuditRepository(path); await repository.AppendAsync(new AuditEntry { AuditId = Guid.NewGuid(), DocumentId = document, UserId = "checker-001", ActorRole = "Checker", DocumentRevision = "2", DocumentVersion = "version-2", TimestampOffset = when, TimestampUtc = when.UtcDateTime, Action = DocumentAuditAction.CheckerSignatureApplied, DetailsJson = "{\"checkerUserId\":\"checker-001\",\"checkerRole\":\"Checker\",\"checkerSignatureMethod\":\"Typed\",\"checkerSignatureTimestampUtc\":\"2026-09-16T10:00:00Z\"}" }); var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document))); Assert.Equal(DocumentAuditAction.CheckerSignatureApplied, record.Action); Assert.Equal("2", record.DocumentRevision); Assert.Equal("version-2", record.DocumentVersion); Assert.Contains("checkerSignatureMethod", record.DetailsJson); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-003 Checker audit details contain metadata and digest but no raw payload")]
    public void Checker_audit_contract_excludes_raw_signature_payload()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs"))); Assert.DoesNotContain("checkerSignatureCanonical = signature.CanonicalRepresentation", source); Assert.Contains("checkerSignatureCanonical = signature?.IsChecker == true ? signature.IntegrityHash : null", source); Assert.Contains("checkerSignatureMethod", source); Assert.Contains("checkerSignatureTimestampUtc", source); Assert.Contains("checkerUserId", source); Assert.Contains("checkerDisplayName", source);
    }

    [Fact(DisplayName = "TD-KAN70-07 VerificationReportGenerated audit round-trips report metadata and normalized scope")]
    public async Task Verification_report_audit_round_trips_history_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-report-{Guid.NewGuid():N}.db"); var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"); var session = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"); var timestamp = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        try { var repository = new SqliteAuditRepository(path); var entry = new AuditEntry { AuditId = Guid.NewGuid(), DocumentId = document, UserId = "checker-id", ActorRole = "Checker", DocumentRevision = "7", DocumentVersion = "model-3", TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, Action = DocumentAuditAction.VerificationReportGenerated, DetailsJson = JsonSerializer.Serialize(new { verificationSessionId = session, documentId = document, drawingVersionId = "drawing-7", modelVersionId = "model-3", reportType = "VerificationReport", reportFormat = "PDF", generatedUtc = timestamp, scope = new { mismatchedAndMissingOnly = true, featureFilters = new[] { "Slot" } } }) }; await repository.AppendAsync(entry); var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document))); Assert.Equal(DocumentAuditAction.VerificationReportGenerated, record.Action); Assert.Equal("checker-id", record.ActorId); Assert.Equal("Checker", record.ActorRole); Assert.Equal("7", record.DocumentRevision); Assert.Equal("model-3", record.DocumentVersion); using var json = JsonDocument.Parse(record.DetailsJson); Assert.Equal(session, json.RootElement.GetProperty("verificationSessionId").GetGuid()); Assert.Equal("VerificationReport", json.RootElement.GetProperty("reportType").GetString()); Assert.Equal("PDF", json.RootElement.GetProperty("reportFormat").GetString()); Assert.True(json.RootElement.GetProperty("scope").GetProperty("mismatchedAndMissingOnly").GetBoolean()); Assert.Equal("Slot", json.RootElement.GetProperty("scope").GetProperty("featureFilters")[0].GetString()); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
    [Fact(DisplayName = "TSD-AUDIT view and download records preserve Viewer identity, version, action, and bounded metadata")]
    public async Task Viewer_access_audit_round_trips_required_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-audit-{Guid.NewGuid():N}.db");
        var document = Guid.NewGuid();
        try
        {
            var repository = new SqliteAuditRepository(path);
            foreach (var action in new[] { DocumentAuditAction.DocumentViewed, DocumentAuditAction.DocumentDownloaded })
                await repository.WriteAsync(new AuditEntry { DocumentId = document, UserId = "viewer-1", ActorRole = SystemRoles.Viewer, DocumentRevision = "2", DocumentVersion = "version-2", Action = action, TimestampUtc = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc), DetailsJson = $"{{\"action\":\"{action}\"}}" });

            var records = await repository.QueryAsync(new AuditLogQuery(DocumentId: document));
            Assert.Equal(2, records.Count);
            Assert.Contains(records, record => record.Action == DocumentAuditAction.DocumentViewed);
            Assert.Contains(records, record => record.Action == DocumentAuditAction.DocumentDownloaded);
            Assert.All(records, record => { Assert.Equal("viewer-1", record.ActorId); Assert.Equal(SystemRoles.Viewer, record.ActorRole); Assert.Equal("2", record.DocumentRevision); Assert.Equal("version-2", record.DocumentVersion); Assert.DoesNotContain("payload", record.DetailsJson, StringComparison.OrdinalIgnoreCase); });
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }

    private static AuditEntry Entry(Guid document, DocumentAuditAction action, string revision, DateTimeOffset timestamp, string metadata = "{}") => new() { AuditId = Guid.NewGuid(), DocumentId = document, UserId = "maker-001", ActorRole = "Maker", DocumentRevision = revision, DocumentVersion = "1", TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, Action = action, DetailsJson = "{\"event\":\"audit\"}", EventMetadataJson = metadata };
}
