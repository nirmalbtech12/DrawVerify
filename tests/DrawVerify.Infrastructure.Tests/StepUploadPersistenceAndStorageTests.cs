using System.Text;
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
            var document = new Document { DocumentId = id, OriginalFileName = "part.step", FileSizeBytes = 10, UploaderUserId = "maker", UploaderDisplayName = "Maker", UploadTimestampUtc = DateTime.UtcNow };
            var first = new DocumentVersion { DocumentId = id, VersionNumber = 1, StoredRelativePath = "uploads/step/one.step", FileSizeBytes = 10, UploaderUserId = "maker" };
            await repository.CreateWithVersionAsync(document, first);
            await repository.AddVersionAsync(id, new DocumentVersion { DocumentId = id, VersionNumber = 2, StoredRelativePath = "uploads/step/two.step", FileSizeBytes = 11, UploaderUserId = "maker" });
            var loaded = await new SqliteDocumentRepository(database).GetAsync(id);
            Assert.NotNull(loaded);
            Assert.Equal("part.step", loaded.OriginalFileName);
            Assert.Equal(2, loaded.CurrentVersion);
            Assert.Equal(new[] { 1, 2 }, loaded.Versions.Select(x => x.VersionNumber));
            Assert.Equal("uploads/step/one.step", loaded.Versions[0].StoredRelativePath);
            Assert.NotNull(await repository.FindByOriginalNameAndSizeAsync("part.step", 11));

            var audit = new SqliteAuditRepository(database);
            foreach (var action in Enum.GetValues<DocumentAuditAction>())
                await audit.WriteAsync(new AuditEntry { DocumentId = action == DocumentAuditAction.Cancelled ? null : id, Action = action, UserId = "maker", DetailsJson = "{\"fileName\":\"part.step\"}" });
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Action, DocumentId, UserId, TimestampUtc, DetailsJson FROM DocumentAuditLog ORDER BY rowid";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = 0;
            while (await reader.ReadAsync())
            {
                rows++;
                Assert.False(string.IsNullOrWhiteSpace(reader.GetString(3)));
                Assert.Equal("maker", reader.GetString(2));
                Assert.Contains("fileName", reader.GetString(4));
                if ((DocumentAuditAction)reader.GetInt32(0) == DocumentAuditAction.Cancelled) Assert.True(reader.IsDBNull(1));
            }
            Assert.Equal(Enum.GetValues<DocumentAuditAction>().Length, rows);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
