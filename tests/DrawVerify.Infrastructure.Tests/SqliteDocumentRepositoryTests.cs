using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteDocumentRepositoryTests
{
    [Fact(DisplayName = "TSD-AC01-UPLOAD SQLite persists an accepted PDF document version")]
    public async Task Accepted_pdf_document_version_round_trips()
    {
        var path = TempPath();
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var id = Guid.NewGuid();
            var version = new DocumentVersion { DocumentId = id, VersionId = Guid.NewGuid(), VersionNumber = 1, StoredRelativePath = "uploads/drawing.pdf", FileSizeBytes = 8 };
            var document = new Document { DocumentId = id, DocumentType = CoreDocumentType.PdfDrawing, OriginalFileName = "drawing.pdf", FileSizeBytes = 8, Version = version, Versions = new[] { version } };
            await repository.CreateWithVersionAsync(document, version);

            var loaded = await repository.GetAsync(id);
            Assert.Equal(CoreDocumentType.PdfDrawing, loaded!.DocumentType);
            Assert.Equal(version.VersionId, loaded.Version!.VersionId);
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-04 legacy document versions remain readable without a fingerprint")]
    public void Legacy_document_version_has_nullable_fingerprint()
    {
        var version = new DocumentVersion { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), VersionNumber = 1 };
        Assert.Null(version.Fingerprint);
    }
    [Fact(DisplayName = "TD-KAN21-INF-001 persists invalid audit actors, details, and null document")]
    public async Task Invalid_step_audit_round_trips()
    {
        var path = TempPath();
        try
        {
            var repository = new SqliteAuditRepository(path);
            var entry = new AuditEntry { Action = DocumentAuditAction.InvalidStepFileRejected, UserId = "MAKER", DetailsJson = "{\"originalFileName\":\"bad.step\",\"fileSizeBytes\":12,\"reason\":\"Invalid header\"}" };
            await repository.WriteAsync(entry);
            await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
            await using var q = c.CreateCommand(); q.CommandText = "SELECT DocumentId,Action,UserId,TimestampUtc,DetailsJson FROM DocumentAuditLog";
            await using var r = await q.ExecuteReaderAsync(); Assert.True(await r.ReadAsync());
            Assert.True(r.IsDBNull(0)); Assert.Equal((int)DocumentAuditAction.InvalidStepFileRejected, r.GetInt32(1)); Assert.Equal("MAKER", r.GetString(2));
            Assert.True(DateTime.TryParse(r.GetString(3), out _)); using var json = JsonDocument.Parse(r.GetString(4)); Assert.Equal("bad.step", json.RootElement.GetProperty("originalFileName").GetString());
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-KAN21-INF-001 migrates legacy audit schema and retains rows indefinitely")]
    public async Task Legacy_schema_gets_nullable_user_id_without_deleting_rows()
    {
        var path = TempPath();
        try
        {
            await using (var c = new SqliteConnection($"Data Source={path}"))
            { await c.OpenAsync(); await using var q = c.CreateCommand(); q.CommandText = "CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); INSERT INTO DocumentAuditLog VALUES ('legacy',NULL,1,'2020-01-01T00:00:00Z','{}');"; await q.ExecuteNonQueryAsync(); }
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry { Action = DocumentAuditAction.InvalidStepFileRejected, UserId = "Anonymous", DetailsJson = "{\"reason\":\"Not a STEP file\"}" });
            await using var check = new SqliteConnection($"Data Source={path}"); await check.OpenAsync();
            await using var q2 = check.CreateCommand(); q2.CommandText = "PRAGMA table_info(DocumentAuditLog)"; var columns = new List<string>(); await using var reader = await q2.ExecuteReaderAsync(); while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            Assert.Contains("UserId", columns); Assert.Equal(2, CountRows(check));
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-02 historical file references select the requested version")]
    public void Historical_file_reference_does_not_fall_back_to_current_version()
    {
        var prior = new DocumentVersion { VersionId = Guid.NewGuid(), VersionNumber = 1, StoredRelativePath = "history/prior.step" };
        var current = new DocumentVersion { VersionId = Guid.NewGuid(), VersionNumber = 2, StoredRelativePath = "current.step" };
        var document = new Document { DocumentId = Guid.NewGuid(), Version = current, Versions = new[] { prior, current } };
        Assert.Equal(prior.StoredRelativePath, document.Versions.Single(x => x.VersionId == prior.VersionId).StoredRelativePath);
        Assert.NotEqual(current.VersionId, prior.VersionId);
    }

    [Fact(DisplayName = "TSD-AC04-VERSION-HISTORY SQLite retains successive PDF/image versions")]
    public async Task Pdf_versions_keep_numbering_and_prior_content()
    {
        var path = TempPath();
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var documentId = Guid.NewGuid();
            var first = new DocumentVersion { DocumentId = documentId, VersionId = Guid.NewGuid(), VersionNumber = 1, StoredRelativePath = "drawing-v1.pdf", FileSizeBytes = 8 };
            var second = new DocumentVersion { DocumentId = documentId, VersionId = Guid.NewGuid(), VersionNumber = 2, StoredRelativePath = "drawing-v2.pdf", FileSizeBytes = 9 };
            var document = new Document { DocumentId = documentId, DocumentType = CoreDocumentType.PdfDrawing, OriginalFileName = "drawing.pdf", FileSizeBytes = 8, CurrentVersion = 1, Version = first, Versions = new[] { first } };

            await repository.CreateWithVersionAsync(document, first);
            await repository.AddVersionAsync(documentId, second);
            var loaded = await repository.GetAsync(documentId);

            Assert.Equal(2, loaded!.CurrentVersion);
            Assert.Equal(first.StoredRelativePath, loaded.Versions.Single(x => x.VersionId == first.VersionId).StoredRelativePath);
            Assert.Equal(second.StoredRelativePath, loaded.Versions.Single(x => x.VersionId == second.VersionId).StoredRelativePath);
        }
        finally { Cleanup(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"drawverify-invalid-audit-{Guid.NewGuid():N}.db");
    private static int CountRows(SqliteConnection c) { using var q = c.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog"; return Convert.ToInt32(q.ExecuteScalar()); }
    private static void Cleanup(string path) { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}
