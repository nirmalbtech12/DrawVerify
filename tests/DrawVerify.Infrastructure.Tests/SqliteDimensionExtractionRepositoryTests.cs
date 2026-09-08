using System.Text;
using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreUploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteDimensionExtractionRepositoryTests
{
    [Fact(DisplayName = "TD-KAN25-INFRA-001 creates additive dimension schema and retains audit rows")]
    public async Task Fresh_schema_persists_dimensions_status_error_and_indefinite_audit_rows()
    {
        var path = TempPath("fresh");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var (document, version) = await SeedAsync(repository);

            var dimension = Dimension(document.DocumentId, version.VersionId, 0, DimensionType.Linear, 10.5, -0.1, 0.2, "10.5 \"quoted\"");
            var audit = Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"durationMilliseconds\":12,\"dimensionCount\":1}");
            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, new[] { dimension }, VerificationStatus.DimensionsExtracted, null, audit);

            var loaded = await new SqliteDocumentRepository(path).GetAsync(document.DocumentId);
            var stored = await new SqliteDocumentRepository(path).GetDimensionsAsync(document.DocumentId);

            Assert.NotNull(loaded);
            Assert.Equal(VerificationStatus.DimensionsExtracted, loaded!.VerificationStatus);
            Assert.Null(loaded.VerificationError);
            Assert.Single(loaded.Versions);
            Assert.Equal(dimension, Assert.Single(stored));
            Assert.Contains("DimensionData", await TableSqlAsync(path));
            Assert.Contains("VerificationError", await ColumnsAsync(path, "Document"));
            Assert.Contains("SourceEntityOrdinal", await ColumnsAsync(path, "DimensionData"));

            var auditRow = await ReadAuditAsync(path, audit.AuditId);
            Assert.Equal(document.DocumentId, auditRow.DocumentId);
            Assert.Equal(DocumentAuditAction.DimensionsExtracted, auditRow.Action);
            Assert.Equal("maker-1", auditRow.UserId);
            using var details = JsonDocument.Parse(auditRow.DetailsJson);
            Assert.Equal(1, details.RootElement.GetProperty("dimensionCount").GetInt32());

            Assert.Equal(0, await new SqliteAuditRetentionRepository(path).ApplyAsync(0));
            Assert.Equal(2L, await CountAsync(path, "DocumentAuditLog"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 migrates a retained pre-dimension schema")]
    public async Task Retained_schema_migration_preserves_documents_versions_and_audits()
    {
        var path = TempPath("migration");
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Document (DocumentId TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, UploadTimestampUtc TEXT NOT NULL, DocumentType INTEGER NOT NULL, UploadStatus INTEGER NOT NULL, VerificationStatus INTEGER NOT NULL DEFAULT 0, GeometryStatus INTEGER NOT NULL, Status INTEGER NOT NULL, CurrentVersion INTEGER NOT NULL, UploaderUserId TEXT NULL, UploaderDisplayName TEXT NULL);
                    CREATE TABLE DocumentVersion (VersionId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, VersionNumber INTEGER NOT NULL, StoredRelativePath TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, FOREIGN KEY(DocumentId) REFERENCES Document(DocumentId));
                    CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL);
                    INSERT INTO Document VALUES ($documentId, 'legacy.dxf', 18, '2025-01-01T00:00:00.0000000Z', 3, 1, 1, 0, 0, 1, 'legacy-user', 'Legacy Maker');
                    INSERT INTO DocumentVersion VALUES ($versionId, $documentId, 1, 'uploads/dxf/legacy.dxf', 18, '2025-01-01T00:00:00.0000000Z');
                    INSERT INTO DocumentAuditLog VALUES ('legacy-audit', $documentId, 1, '2025-01-01T00:00:00.0000000Z', '{"retained":true}');
                    """;
                command.Parameters.AddWithValue("$documentId", documentId.ToString());
                command.Parameters.AddWithValue("$versionId", versionId.ToString());
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SqliteDocumentRepository(path);
            var loaded = await repository.GetAsync(documentId);

            Assert.NotNull(loaded);
            Assert.Equal("legacy.dxf", loaded!.OriginalFileName);
            Assert.Equal(CoreDocumentType.DxfDrawing, loaded.DocumentType);
            Assert.Equal("legacy-user", loaded.UploaderUserId);
            Assert.Equal(versionId, Assert.Single(loaded.Versions).VersionId);
            Assert.Contains("VerificationError", await ColumnsAsync(path, "Document"));
            Assert.Contains("UserId", await ColumnsAsync(path, "DocumentAuditLog"));
            Assert.Contains("DimensionData", await TableSqlAsync(path));
            Assert.Equal(1L, await CountAsync(path, "DocumentAuditLog"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 persists nullable tolerance and parameterized label data")]
    public async Task Dimension_rows_round_trip_nullable_values_and_quotes()
    {
        var path = TempPath("values");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var (document, version) = await SeedAsync(repository);
            var dimensions = new[]
            {
                Dimension(document.DocumentId, version.VersionId, 2, DimensionType.Radial, 8, null, null, "R8"),
                Dimension(document.DocumentId, version.VersionId, 0, DimensionType.Angular, 45, -0.1, 0.2, "angle 'quoted'"),
                Dimension(document.DocumentId, version.VersionId, 1, DimensionType.Linear, 20, null, null, null)
            };

            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, dimensions, VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":3}"));

            var loaded = await repository.GetDimensionsAsync(document.DocumentId);

            Assert.Equal(new[] { 0, 1, 2 }, loaded.Select(dimension => dimension.SourceEntityOrdinal));
            Assert.Equal(DimensionType.Angular, loaded[0].DimensionType);
            Assert.Equal(-0.1, loaded[0].LowerTolerance);
            Assert.Equal(0.2, loaded[0].UpperTolerance);
            Assert.Equal("angle 'quoted'", loaded[0].DisplayText);
            Assert.Null(loaded[1].DisplayText);
            Assert.Null(loaded[2].LowerTolerance);
            Assert.Equal(8, loaded[2].NominalValue);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 replaces only the current document-version dimensions")]
    public async Task Re_extraction_replaces_rows_without_new_versions_and_preserves_history()
    {
        var path = TempPath("replacement");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var (document, firstVersion) = await SeedAsync(repository);
            await repository.ReplaceDimensionsAsync(document.DocumentId, firstVersion.VersionId,
                new[] { Dimension(document.DocumentId, firstVersion.VersionId, 0, DimensionType.Linear, 10) },
                VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));

            var secondVersion = new DocumentVersion
            {
                DocumentId = document.DocumentId,
                VersionNumber = 2,
                StoredRelativePath = "uploads/dxf/second.dxf",
                FileSizeBytes = 20
            };
            await repository.AddVersionAsync(document.DocumentId, secondVersion);
            Assert.Empty(await repository.GetDimensionsAsync(document.DocumentId));

            var firstResult = Dimension(document.DocumentId, secondVersion.VersionId, 4, DimensionType.Linear, 12, displayText: "12");
            await repository.ReplaceDimensionsAsync(document.DocumentId, secondVersion.VersionId, new[] { firstResult },
                VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));
            var replacement = new[] { Dimension(document.DocumentId, secondVersion.VersionId, 1, DimensionType.Radial, 4, displayText: "R4") };
            await repository.ReplaceDimensionsAsync(document.DocumentId, secondVersion.VersionId, replacement,
                VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));

            var current = await repository.GetDimensionsAsync(document.DocumentId);
            var loaded = await repository.GetAsync(document.DocumentId);

            Assert.Equal(replacement, current);
            Assert.Equal(2, loaded!.Versions.Count);
            Assert.Equal(2, loaded.CurrentVersion);
            Assert.Equal(2L, await CountAsync(path, "DimensionData"));
            Assert.Equal(4L, await CountAsync(path, "DocumentAuditLog"));
            Assert.Equal(2, await CountVersionsAsync(path, document.DocumentId));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 clears current dimensions for successful empty and failed outcomes")]
    public async Task Empty_success_and_failure_store_their_status_and_error_contracts()
    {
        var path = TempPath("outcomes");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var (document, version) = await SeedAsync(repository);
            var existing = Dimension(document.DocumentId, version.VersionId, 0, DimensionType.Linear, 10);
            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, new[] { existing }, VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));

            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, Array.Empty<DimensionData>(), VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":0}"));
            Assert.Empty(await repository.GetDimensionsAsync(document.DocumentId));

            var staleBeforeFailure = Dimension(document.DocumentId, version.VersionId, 3, DimensionType.Angular, 25, displayText: "25 degrees");
            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, new[] { staleBeforeFailure }, VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));
            Assert.Equal(staleBeforeFailure, Assert.Single(await repository.GetDimensionsAsync(document.DocumentId)));

            const string error = "Dimension extraction failed: unable to parse DXF file.";
            var failedAudit = Audit(document.DocumentId, DocumentAuditAction.DimensionExtractionFailed, "maker-1",
                "{\"durationMilliseconds\":12,\"dimensionCount\":0,\"warnings\":[],\"warningCount\":0,\"error\":\"Dimension extraction failed: unable to parse DXF file.\"}");
            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, Array.Empty<DimensionData>(), VerificationStatus.Error, error,
                failedAudit);

            var loaded = await new SqliteDocumentRepository(path).GetAsync(document.DocumentId);
            Assert.Empty(await repository.GetDimensionsAsync(document.DocumentId));
            Assert.Equal(VerificationStatus.Error, loaded!.VerificationStatus);
            Assert.Equal(error, loaded.VerificationError);
            Assert.Single(loaded.Versions);
            Assert.Equal(1, await CountVersionsAsync(path, document.DocumentId));
            Assert.Equal(5L, await CountAsync(path, "DocumentAuditLog"));
            var auditRow = await ReadAuditAsync(path, failedAudit.AuditId);
            Assert.Equal(document.DocumentId, auditRow.DocumentId);
            Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, auditRow.Action);
            Assert.Equal("maker-1", auditRow.UserId);
            using var details = JsonDocument.Parse(auditRow.DetailsJson);
            Assert.Equal(12, details.RootElement.GetProperty("durationMilliseconds").GetInt64());
            Assert.Equal(0, details.RootElement.GetProperty("dimensionCount").GetInt32());
            Assert.Equal(error, details.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 rolls back replacement, status, and audit together")]
    public async Task Audit_failure_rolls_back_dimension_replacement_and_status_update()
    {
        var path = TempPath("rollback");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var (document, version) = await SeedAsync(repository);
            var existing = Dimension(document.DocumentId, version.VersionId, 0, DimensionType.Linear, 10);
            await repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, new[] { existing }, VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}"));

            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER FailDimensionAudit BEFORE INSERT ON DocumentAuditLog WHEN NEW.Action = 9 BEGIN SELECT RAISE(ABORT, 'injected audit failure'); END;";
                await command.ExecuteNonQueryAsync();
            }

            var replacement = Dimension(document.DocumentId, version.VersionId, 1, DimensionType.Radial, 4);
            await Assert.ThrowsAsync<SqliteException>(() => repository.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, new[] { replacement },
                VerificationStatus.DimensionsExtracted, null,
                Audit(document.DocumentId, DocumentAuditAction.DimensionsExtracted, "maker-1", "{\"dimensionCount\":1}")));

            var loaded = await new SqliteDocumentRepository(path).GetAsync(document.DocumentId);
            var dimensions = await repository.GetDimensionsAsync(document.DocumentId);
            Assert.Equal(VerificationStatus.DimensionsExtracted, loaded!.VerificationStatus);
            Assert.Equal(existing, Assert.Single(dimensions));
            Assert.Equal(2L, await CountAsync(path, "DocumentAuditLog"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact(DisplayName = "TD-KAN25-INFRA-001 opens only contained asynchronous DXF paths")]
    public async Task Dxf_read_access_rejects_blank_rooted_and_escaping_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-dimension-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DxfDocumentFileStore(root);
            await using (var staging = await store.CreateStagingFileAsync("drawing.dxf"))
            {
                await staging.Stream.WriteAsync(Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n"));
                var relativePath = await store.MoveToFinalAsync(staging);
                await using var read = await store.OpenReadAsync(relativePath);
                using var reader = new StreamReader(read, Encoding.ASCII, leaveOpen: false);
                Assert.Contains("ENTITIES", await reader.ReadToEndAsync());
            }

            await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync("   "));
            await Assert.ThrowsAsync<IOException>(() => store.OpenReadAsync("../outside.dxf"));
            await Assert.ThrowsAsync<IOException>(() => store.OpenReadAsync(Path.Combine(root, "outside.dxf")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(Document Document, DocumentVersion Version)> SeedAsync(SqliteDocumentRepository repository)
    {
        var documentId = Guid.NewGuid();
        var version = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = 1,
            StoredRelativePath = "uploads/dxf/drawing.dxf",
            FileSizeBytes = 128
        };
        var document = new Document
        {
            DocumentId = documentId,
            OriginalFileName = "drawing.dxf",
            FileSizeBytes = 128,
            DocumentType = CoreDocumentType.DxfDrawing,
            UploadStatus = CoreUploadStatus.Uploaded,
            VerificationStatus = VerificationStatus.PendingVerification,
            Version = version,
            Versions = new[] { version }
        };
        await repository.CreateWithVersionAndAuditAsync(document, version,
            Audit(documentId, DocumentAuditAction.Uploaded, "maker-1", "{\"fileSizeBytes\":128}"));
        return (document, version);
    }

    private static DimensionData Dimension(Guid documentId, Guid versionId, int ordinal, DimensionType type, double nominal, double? lower = null, double? upper = null, string? displayText = null) =>
        new(documentId, versionId, ordinal, type, nominal, lower, upper, displayText);

    private static AuditEntry Audit(Guid documentId, DocumentAuditAction action, string userId, string details) => new()
    {
        DocumentId = documentId,
        Action = action,
        UserId = userId,
        TimestampUtc = DateTime.UtcNow,
        DetailsJson = details
    };

    private static string TempPath(string suffix) => Path.Combine(Path.GetTempPath(), $"drawverify-dimensions-{suffix}-{Guid.NewGuid():N}.db");

    private static async Task<long> CountAsync(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountVersionsAsync(string path, Guid documentId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DocumentVersion WHERE DocumentId=$id";
        command.Parameters.AddWithValue("$id", documentId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<HashSet<string>> ColumnsAsync(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task<string> TableSqlAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GROUP_CONCAT(name || ':' || sql, char(10)) FROM sqlite_master WHERE type='table' OR type='index'";
        return (string?)await command.ExecuteScalarAsync() ?? string.Empty;
    }

    private static async Task<(Guid? DocumentId, DocumentAuditAction Action, string UserId, string DetailsJson)> ReadAuditAsync(string path, Guid auditId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DocumentId,Action,UserId,DetailsJson FROM DocumentAuditLog WHERE AuditId=$id";
        command.Parameters.AddWithValue("$id", auditId.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0)), (DocumentAuditAction)reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path))
            File.Delete(path);
    }
}
