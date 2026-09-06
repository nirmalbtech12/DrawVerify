using System.Text;
using Microsoft.Data.Sqlite;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class StepUploadPersistenceAndStorageTests
{
    [Fact]
    public async Task Storage_uses_contained_guid_prefixed_sanitized_names_and_retains_original_name()
    {
        var root = NewDirectory();
        try
        {
            var store = new DocumentFileStore(root, new StepUploadConfiguration());
            await using var staging = await store.CreateStagingFileAsync(".." + Path.DirectorySeparatorChar + "bad" + Path.DirectorySeparatorChar + "part.step");
            await staging.Stream.WriteAsync(Encoding.UTF8.GetBytes("step"));
            var relative = await store.MoveToFinalAsync(staging);
            var finalName = Path.GetFileName(relative);
            Assert.Matches("^[0-9a-f]{32}_.+\\.step$", finalName);
            var full = Path.GetFullPath(Path.Combine(root, "uploads/step", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(full));
            Assert.StartsWith(Path.GetFullPath(Path.Combine(root, "uploads/step")) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
            var document = new Document { OriginalFileName = ".." + Path.DirectorySeparatorChar + "bad" + Path.DirectorySeparatorChar + "part.step" };
            Assert.Equal(".." + Path.DirectorySeparatorChar + "bad" + Path.DirectorySeparatorChar + "part.step", document.OriginalFileName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Storage_limits_long_physical_names_and_rejects_empty_sanitized_names()
    {
        var root = NewDirectory();
        try
        {
            var store = new DocumentFileStore(root);
            var longName = new string('a', 200) + ".step";
            await using var staging = await store.CreateStagingFileAsync(longName);
            Assert.True(Path.GetFileName(staging.RelativePath).Length <= 128);
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateStagingFileAsync("   "));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Sqlite_repositories_persist_document_versions_and_all_audit_actions()
    {
        var root = NewDirectory();
        var database = Path.Combine(root, "documents.db");
        try
        {
            var repository = new SqliteDocumentRepository(database);
            var id = Guid.NewGuid();
            var document = new Document { DocumentId = id, OriginalFileName = "part.step", FileSizeBytes = 10, UploadTimestampUtc = DateTime.UtcNow };
            var first = new DocumentVersion { DocumentId = id, VersionNumber = 1, StoredRelativePath = "uploads/step/one.step", FileSizeBytes = 10 };
            await repository.CreateWithVersionAsync(document, first);
            await repository.AddVersionAsync(id, new DocumentVersion { DocumentId = id, VersionNumber = 2, StoredRelativePath = "uploads/step/two.step", FileSizeBytes = 11 });
            var loaded = await new SqliteDocumentRepository(database).GetAsync(id);
            Assert.NotNull(loaded);
            Assert.Equal("part.step", loaded.OriginalFileName);
            Assert.Equal(2, loaded.CurrentVersion);
            Assert.Equal(new[] { 1, 2 }, loaded.Versions.Select(x => x.VersionNumber));
            Assert.Equal("uploads/step/one.step", loaded.Versions[0].StoredRelativePath);
            Assert.NotNull(await repository.FindByOriginalNameAndSizeAsync("part.step", 11));

            var audit = new SqliteAuditRepository(database);
            foreach (var action in Enum.GetValues<DocumentAuditAction>())
                await audit.WriteAsync(new AuditEntry { DocumentId = action == DocumentAuditAction.Cancelled ? null : id, Action = action, DetailsJson = "{\"fileName\":\"part.step\"}" });
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT Action, DocumentId, TimestampUtc, DetailsJson FROM DocumentAuditLog ORDER BY rowid";
                await using var reader = await command.ExecuteReaderAsync();
                var rows = 0;
                while (await reader.ReadAsync())
                {
                    rows++;
                    Assert.False(string.IsNullOrWhiteSpace(reader.GetString(2)));
                    Assert.Contains("fileName", reader.GetString(3));
                    if ((DocumentAuditAction)reader.GetInt32(0) == DocumentAuditAction.Cancelled) Assert.True(reader.IsDBNull(1));
                }
                Assert.Equal(Enum.GetValues<DocumentAuditAction>().Length, rows);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
    }

    [Fact(DisplayName = "TD-KAN116-INF-001 migrates legacy user provenance schema idempotently")]
    public async Task Sqlite_migration_preserves_retained_records_and_removes_user_columns()
    {
        var root = NewDirectory();
        var database = Path.Combine(root, "legacy.db");
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Document (DocumentId TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, UploadTimestampUtc TEXT NOT NULL, UploaderUserId TEXT NOT NULL, UploaderDisplayName TEXT NOT NULL, DocumentType INTEGER NOT NULL, UploadStatus INTEGER NOT NULL, GeometryStatus INTEGER NOT NULL, Status INTEGER NOT NULL, CurrentVersion INTEGER NOT NULL);
                    CREATE TABLE DocumentVersion (VersionId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, VersionNumber INTEGER NOT NULL, StoredRelativePath TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, UploaderUserId TEXT NOT NULL, FOREIGN KEY(DocumentId) REFERENCES Document(DocumentId));
                    CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL);
                    INSERT INTO Document VALUES ($documentId, 'legacy.step', 42, '2026-01-02T03:04:05.0000000Z', 'removed-user', 'Removed User', 1, 1, 0, 0, 1);
                    INSERT INTO DocumentVersion VALUES ($versionId, $documentId, 1, 'uploads/legacy.step', 42, '2026-01-02T03:04:05.0000000Z', 'removed-user');
                    INSERT INTO DocumentAuditLog VALUES ($auditId, $documentId, 0, 'removed-user', '2026-01-02T03:04:05.0000000Z', '{"detail":"retained"}');
                    """;
                command.Parameters.AddWithValue("$documentId", documentId.ToString());
                command.Parameters.AddWithValue("$versionId", versionId.ToString());
                command.Parameters.AddWithValue("$auditId", auditId.ToString());
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SqliteDocumentRepository(database);
            var loaded = await repository.GetAsync(documentId);
            Assert.NotNull(loaded);
            Assert.Equal("legacy.step", loaded.OriginalFileName);
            Assert.Equal(42, loaded.FileSizeBytes);
            Assert.Equal("uploads/legacy.step", Assert.Single(loaded.Versions).StoredRelativePath);

            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                foreach (var table in new[] { "Document", "DocumentVersion", "DocumentAuditLog" })
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"PRAGMA table_info({table})";
                    await using var reader = await command.ExecuteReaderAsync();
                    var columns = new List<string>();
                    while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
                    Assert.DoesNotContain("UserId", columns);
                    Assert.DoesNotContain("UploaderUserId", columns);
                    Assert.DoesNotContain("UploaderDisplayName", columns);
                }

                await using var auditCommand = connection.CreateCommand();
                auditCommand.CommandText = "SELECT AuditId, DocumentId, DetailsJson FROM DocumentAuditLog";
                await using var auditReader = await auditCommand.ExecuteReaderAsync();
                Assert.True(await auditReader.ReadAsync());
                Assert.Equal(auditId.ToString(), auditReader.GetString(0));
                Assert.Equal(documentId.ToString(), auditReader.GetString(1));
                Assert.Contains("retained", auditReader.GetString(2));
            }

            var repeat = new SqliteDocumentRepository(database);
            var reloaded = await repeat.GetAsync(documentId);
            Assert.NotNull(reloaded);
            Assert.Single(reloaded.Versions);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
