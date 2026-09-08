using System.Text;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class DxfUploadPersistenceAndStorageTests
{
    [Fact(DisplayName = "TD-KAN23-INFRA-001 storage is contained, sanitized, and GUID prefixed")]
    public async Task Dxf_storage_is_safe_and_retains_original_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-dxf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DxfDocumentFileStore(root);
            const string original = "..\\unsafe:name?.dxf";
            await using var staging = await store.CreateStagingFileAsync(original);
            await staging.Stream.WriteAsync(Encoding.ASCII.GetBytes("0\nSECTION\n0\nEOF\n"));
            var relative = await store.MoveToFinalAsync(staging);
            Assert.Matches("^[0-9a-f]{32}_.+\\.dxf$", Path.GetFileName(relative));
            var full = Path.GetFullPath(Path.Combine(root, "uploads/dxf", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(full));
            Assert.StartsWith(Path.GetFullPath(Path.Combine(root, "uploads/dxf")) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateStagingFileAsync("   "));
            await store.DeleteAsync(relative);
            Assert.False(File.Exists(full));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact(DisplayName = "TD-KAN23-INFRA-001 SQLite reload preserves DXF metadata, versions, and audit retention")]
    public async Task Sqlite_persists_dxf_metadata_versions_and_audit_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "documents.db");
        try
        {
            var repository = new SqliteDocumentRepository(database);
            var id = Guid.NewGuid();
            var document = new Document { DocumentId = id, OriginalFileName = "drawing.dxf", FileSizeBytes = 20, UploadTimestampUtc = DateTime.UtcNow, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, VerificationStatus = VerificationStatus.PendingVerification, UploaderUserId = "user-1", UploaderDisplayName = "Maker" };
            var version = new DocumentVersion { DocumentId = id, VersionNumber = 1, StoredRelativePath = "uploads/dxf/one.dxf", FileSizeBytes = 20 };
            await repository.CreateWithVersionAndAuditAsync(document, version, new AuditEntry { DocumentId = id, UserId = "user-1", Action = DocumentAuditAction.Uploaded, DetailsJson = "{\"fileSizeBytes\":20}" });
            await new SqliteAuditRepository(database).WriteAsync(new AuditEntry { DocumentId = id, UserId = "user-1", Action = DocumentAuditAction.DuplicateDetected, DetailsJson = "{}" });
            var loaded = await new SqliteDocumentRepository(database).GetAsync(id);
            Assert.NotNull(loaded);
            Assert.Equal(DrawVerify.Core.Enums.DocumentType.DxfDrawing, loaded!.DocumentType);
            Assert.Equal(VerificationStatus.PendingVerification, loaded.VerificationStatus);
            Assert.Equal("user-1", loaded.UploaderUserId);
            Assert.Equal("Maker", loaded.UploaderDisplayName);
            Assert.Equal("uploads/dxf/one.dxf", Assert.Single(loaded.Versions).StoredRelativePath);
            await using var connection = new SqliteConnection($"Data Source={database}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog";
            Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
