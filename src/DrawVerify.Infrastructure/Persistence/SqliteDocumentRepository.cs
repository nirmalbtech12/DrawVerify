using System.Globalization;
using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteDocumentRepository : IDocumentRepository, ITransactionalDocumentRepository
{
    private const string DocumentColumns = "DocumentId,OriginalFileName,FileSizeBytes,UploadTimestampUtc,DocumentType,UploadStatus,VerificationStatus,VerificationError,GeometryStatus,Status,CurrentVersion,DrawingSource";
    private const string DocumentColumnsWithUploader = DocumentColumns + ",UploaderUserId,UploaderDisplayName";
    private readonly string _connectionString;
    private readonly Dictionary<Guid, DocumentVersion> _measurementVersions = new();
    private readonly bool _documentHasUploaderColumns;

    public SqliteDocumentRepository(string databasePath, ICentralizedLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        try
        {
            _documentHasUploaderColumns = Initialize();
        }
        catch (Exception exception)
        {
            logger?.Log(exception, "SQLite startup migration", "Unable to migrate retained document, version, and audit schema");
            throw;
        }
    }

    private bool Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, null, "PRAGMA foreign_keys=OFF");
        using var transaction = connection.BeginTransaction();
        try
        {
            var hasUploaderColumns = false;
            var documentWasRebuilt = false;
            if (!Exists(connection, transaction, "Document"))
            {
                Execute(connection, transaction, CreateDocumentSql(includeUploaderColumns: true));
                hasUploaderColumns = true;
            }

            else
            {
                var columns = GetColumnOrdinals(connection, transaction, "Document");
                var hasLegacyIdentityColumns = columns.TryGetValue("UploaderUserId", out var uploaderUserIdOrdinal)
                    && columns.TryGetValue("DocumentType", out var documentTypeOrdinal)
                    && uploaderUserIdOrdinal < documentTypeOrdinal;
                if (hasLegacyIdentityColumns)
                {
                    // These columns belonged to the removed identity provenance model. Keep the
                    // retained document data while leaving the legacy schema compatible with KAN-116.
                    RebuildDocumentWithoutRemovedIdentityColumns(
                        connection,
                        transaction,
                        columns.ContainsKey("VerificationStatus"));
                    documentWasRebuilt = true;
                }
                else
                {
                    AddColumnIfMissing(connection, transaction, "Document", "VerificationStatus", "INTEGER NOT NULL DEFAULT 0");
                    AddColumnIfMissing(connection, transaction, "Document", "VerificationError", "TEXT NULL");
                    AddColumnIfMissing(connection, transaction, "Document", "UploaderUserId", "TEXT NULL");
                    AddColumnIfMissing(connection, transaction, "Document", "UploaderDisplayName", "TEXT NULL");
                    AddColumnIfMissing(connection, transaction, "Document", "DrawingSource", "INTEGER NOT NULL DEFAULT 1");
                    hasUploaderColumns = true;
                }
            }

            AddColumnIfMissing(connection, transaction, "Document", "DrawingSource", "INTEGER NOT NULL DEFAULT 1");

            if (!Exists(connection, transaction, "DocumentVersion"))
            {
                Execute(connection, transaction, CreateDocumentVersionSql());
            }
            else if (documentWasRebuilt || GetColumns(connection, transaction, "DocumentVersion").Contains("UploaderUserId"))
            {
                RebuildDocumentVersionWithoutRemovedIdentityColumns(connection, transaction);
            }
            AddColumnIfMissing(connection, transaction, "DocumentVersion", "DrawingSource", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, transaction, "DocumentVersion", "FingerprintJson", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "DocumentVersion", "FingerprintAlgorithm", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "DocumentVersion", "FingerprintCanonicalizationVersion", "INTEGER NULL");
            Execute(connection, transaction, "CREATE TRIGGER IF NOT EXISTS TR_DocumentVersion_FingerprintUpdate BEFORE UPDATE OF FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion ON DocumentVersion WHEN OLD.FingerprintJson IS NOT NULL AND (NEW.FingerprintJson IS NOT OLD.FingerprintJson OR NEW.FingerprintAlgorithm IS NOT OLD.FingerprintAlgorithm OR NEW.FingerprintCanonicalizationVersion IS NOT OLD.FingerprintCanonicalizationVersion) BEGIN SELECT RAISE(ABORT, 'Document version fingerprints are immutable.'); END; CREATE TRIGGER IF NOT EXISTS TR_DocumentVersion_FingerprintDelete BEFORE DELETE ON DocumentVersion WHEN OLD.FingerprintJson IS NOT NULL BEGIN SELECT RAISE(ABORT, 'Document version fingerprints are immutable.'); END;");

            if (!Exists(connection, transaction, "DocumentAuditLog"))
            {
                Execute(connection, transaction, CreateAuditSql());
            }
            else
            {
                AddColumnIfMissing(connection, transaction, "DocumentAuditLog", "UserId", "TEXT NULL");
            }

            if (!Exists(connection, transaction, "DimensionData"))
                Execute(connection, transaction, CreateDimensionDataSql());
            else
            {
                AddColumnIfMissing(connection, transaction, "DimensionData", "RadialKind", "INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing(connection, transaction, "DimensionData", "Layer", "TEXT NULL");
                AddColumnIfMissing(connection, transaction, "DimensionData", "PreviewAnchor", "TEXT NULL");
                AddColumnIfMissing(connection, transaction, "DimensionData", "BlockName", "TEXT NULL");
                AddColumnIfMissing(connection, transaction, "DimensionData", "ViewName", "TEXT NULL");
                AddColumnIfMissing(connection, transaction, "DimensionData", "LocationX", "REAL NULL");
                AddColumnIfMissing(connection, transaction, "DimensionData", "LocationY", "REAL NULL");
            }
            Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DimensionData_VersionOrdinal ON DimensionData(DocumentVersionId,SourceEntityOrdinal)");

            Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Document_NameSize ON Document(OriginalFileName,FileSizeBytes)");
            transaction.Commit();
            SqliteAuditRepository.EnsureSchema(connection);
            return hasUploaderColumns;
        }
        catch
        {
            try { transaction.Rollback(); }
            finally { Execute(connection, null, "PRAGMA foreign_keys=ON"); }
            throw;
        }
        finally
        {
            Execute(connection, null, "PRAGMA foreign_keys=ON");
        }
    }

    public async Task<Document?> FindByOriginalNameAndSizeAsync(string originalFileName, long fileSizeBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {(_documentHasUploaderColumns ? DocumentColumnsWithUploader : DocumentColumns)} FROM Document WHERE OriginalFileName=$name AND FileSizeBytes=$size LIMIT 1";
        command.Parameters.AddWithValue("$name", originalFileName);
        command.Parameters.AddWithValue("$size", fileSizeBytes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? await MapAsync(reader, connection, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {(_documentHasUploaderColumns ? DocumentColumnsWithUploader : DocumentColumns)} FROM Document WHERE DocumentId=$id";
        command.Parameters.AddWithValue("$id", documentId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? await MapAsync(reader, connection, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertDocumentAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await UpdateCurrentVersionAsync(connection, transaction, documentId, version, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Save(Document document, DocumentVersion version) => CreateWithVersionAsync(document, version).GetAwaiter().GetResult();

    public DocumentVersion? GetCurrentVersion(Guid documentId)
    {
        var version = GetAsync(documentId).GetAwaiter().GetResult()?.Version;
        return version is not null && _measurementVersions.TryGetValue(version.VersionId, out var measured) ? measured : version;
    }

    public async Task ReplaceDimensionsAsync(
        Guid documentId,
        Guid documentVersionId,
        IReadOnlyCollection<DimensionData> dimensions,
        DrawVerify.Core.Enums.VerificationStatus status,
        string? verificationError,
        AuditEntry auditEntry,
        CancellationToken cancellationToken = default)
        => await ReplaceDimensionsAsync(documentId, documentVersionId, dimensions, status, verificationError, () => auditEntry, cancellationToken).ConfigureAwait(false);

    public async Task ReplaceDimensionsAsync(
        Guid documentId,
        Guid documentVersionId,
        IReadOnlyCollection<DimensionData> dimensions,
        DrawVerify.Core.Enums.VerificationStatus status,
        string? verificationError,
        Func<AuditEntry> auditEntryFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(auditEntryFactory);
        if (status == DrawVerify.Core.Enums.VerificationStatus.Error && dimensions.Count != 0)
            throw new InvalidOperationException("A failed dimension extraction cannot persist dimension data.");
        if (status != DrawVerify.Core.Enums.VerificationStatus.Error && verificationError is not null)
            throw new InvalidOperationException("A successful dimension extraction cannot persist an error.");
        if (dimensions.Any(dimension => dimension.DocumentId != documentId || dimension.DocumentVersionId != documentVersionId))
            throw new InvalidOperationException("Dimension data must reference the requested document version.");
        if (dimensions.Select(dimension => dimension.SourceEntityOrdinal).Distinct().Count() != dimensions.Count)
            throw new InvalidOperationException("Dimension source entity ordinals must be unique within a document version.");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var current = connection.CreateCommand())
            {
                current.Transaction = transaction;
                current.CommandText = "SELECT COUNT(*) FROM Document d INNER JOIN DocumentVersion v ON v.DocumentId=d.DocumentId AND v.VersionNumber=d.CurrentVersion WHERE d.DocumentId=$documentId AND v.VersionId=$versionId";
                current.Parameters.AddWithValue("$documentId", documentId.ToString());
                current.Parameters.AddWithValue("$versionId", documentVersionId.ToString());
                if (Convert.ToInt32(await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("The requested document version is not current.");
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM DimensionData WHERE DocumentId=$documentId AND DocumentVersionId=$versionId";
                delete.Parameters.AddWithValue("$documentId", documentId.ToString());
                delete.Parameters.AddWithValue("$versionId", documentVersionId.ToString());
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var dimension in dimensions)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO DimensionData (DimensionDataId,DocumentId,DocumentVersionId,SourceEntityOrdinal,DimensionType,NominalValue,LowerTolerance,UpperTolerance,DisplayText,RadialKind,Layer,PreviewAnchor,BlockName,ViewName,LocationX,LocationY) VALUES ($id,$documentId,$versionId,$ordinal,$type,$nominal,$lower,$upper,$text,$radialKind,$layer,$anchor,$block,$view,$x,$y)";
                Add(insert, "$id", Guid.NewGuid());
                Add(insert, "$documentId", dimension.DocumentId);
                Add(insert, "$versionId", dimension.DocumentVersionId);
                Add(insert, "$ordinal", dimension.SourceEntityOrdinal);
                Add(insert, "$type", dimension.DimensionType);
                Add(insert, "$nominal", dimension.NominalValue);
                insert.Parameters.AddWithValue("$lower", (object?)dimension.LowerTolerance ?? DBNull.Value);
                insert.Parameters.AddWithValue("$upper", (object?)dimension.UpperTolerance ?? DBNull.Value);
                insert.Parameters.AddWithValue("$text", (object?)dimension.DisplayText ?? DBNull.Value);
                Add(insert, "$radialKind", dimension.RadialKind);
                insert.Parameters.AddWithValue("$layer", (object?)dimension.Layer ?? DBNull.Value);
                insert.Parameters.AddWithValue("$anchor", (object?)dimension.PreviewAnchor ?? DBNull.Value);
                insert.Parameters.AddWithValue("$block", (object?)dimension.BlockName ?? DBNull.Value);
                insert.Parameters.AddWithValue("$view", (object?)dimension.ViewName ?? DBNull.Value);
                insert.Parameters.AddWithValue("$x", (object?)dimension.LocationX ?? DBNull.Value);
                insert.Parameters.AddWithValue("$y", (object?)dimension.LocationY ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Document SET VerificationStatus=$status,VerificationError=$error WHERE DocumentId=$documentId";
                Add(update, "$status", status);
                update.Parameters.AddWithValue("$error", (object?)verificationError ?? DBNull.Value);
                update.Parameters.AddWithValue("$documentId", documentId.ToString());
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The document disappeared while dimension data was being persisted.");
            }

            var auditEntry = auditEntryFactory();
            ArgumentNullException.ThrowIfNull(auditEntry);
            if (auditEntry.DocumentId != documentId)
                throw new InvalidOperationException("The dimension audit entry must reference the same document.");
            await InsertAuditAsync(connection, transaction, auditEntry, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async Task<IReadOnlyList<DimensionData>> GetDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT d.DimensionDataId,d.DocumentId,d.DocumentVersionId,d.SourceEntityOrdinal,d.DimensionType,d.NominalValue,d.LowerTolerance,d.UpperTolerance,d.DisplayText,d.RadialKind,d.Layer,d.PreviewAnchor,d.BlockName,d.ViewName,d.LocationX,d.LocationY FROM DimensionData d INNER JOIN Document doc ON doc.DocumentId=d.DocumentId INNER JOIN DocumentVersion v ON v.VersionId=d.DocumentVersionId AND v.DocumentId=doc.DocumentId AND v.VersionNumber=doc.CurrentVersion WHERE d.DocumentId=$documentId ORDER BY d.SourceEntityOrdinal";
        command.Parameters.AddWithValue("$documentId", documentId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dimensions = new List<DimensionData>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            dimensions.Add(new DimensionData(
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                (DrawVerify.Core.Enums.DimensionType)reader.GetInt32(4),
                reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                 (DrawVerify.Core.Enums.RadialDimensionKind)reader.GetInt32(9),
                 reader.IsDBNull(10) ? null : reader.GetString(10),
                 reader.IsDBNull(11) ? null : reader.GetString(11),
                 reader.IsDBNull(12) ? null : reader.GetString(12),
                 reader.IsDBNull(13) ? null : reader.GetString(13),
                 reader.IsDBNull(14) ? null : reader.GetDouble(14),
                 reader.IsDBNull(15) ? null : reader.GetDouble(15)));
        }
        return dimensions;
    }

    public void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, DrawVerify.Core.Enums.GeometryStatus status, string? error)
    {
        var version = _measurementVersions.TryGetValue(versionId, out var existing) ? existing : new DocumentVersion { VersionId = versionId };
        version.Geometry = geometry;
        version.Features.Clear();
        version.Features.AddRange(features);
        version.GeometryStatus = status;
        version.GeometryError = error;
        _measurementVersions[versionId] = version;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Document SET GeometryStatus=$status WHERE DocumentId=(SELECT DocumentId FROM DocumentVersion WHERE VersionId=$versionId)";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$versionId", versionId.ToString());
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"The document version '{versionId}' was not found while persisting measurement status.");
    }

    public async Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertDocumentAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(connection, transaction, auditEntry, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await UpdateCurrentVersionAsync(connection, transaction, documentId, version, cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(connection, transaction, auditEntry, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var statement in new[]
        {
            "DELETE FROM DimensionData WHERE DocumentId=$id",
            "DELETE FROM DocumentVersion WHERE DocumentId=$id",
            "DELETE FROM Document WHERE DocumentId=$id"
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.Parameters.AddWithValue("$id", documentId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertDocumentAsync(SqliteConnection connection, SqliteTransaction transaction, Document document, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _documentHasUploaderColumns
            ? "INSERT INTO Document (DocumentId,OriginalFileName,FileSizeBytes,UploadTimestampUtc,DocumentType,UploadStatus,VerificationStatus,VerificationError,GeometryStatus,Status,CurrentVersion,DrawingSource,UploaderUserId,UploaderDisplayName) VALUES ($id,$name,$size,$timestamp,$type,$upload,$verification,$verificationError,$geometry,$status,$version,$source,$uploaderId,$uploaderName)"
            : "INSERT INTO Document (DocumentId,OriginalFileName,FileSizeBytes,UploadTimestampUtc,DocumentType,UploadStatus,VerificationStatus,VerificationError,GeometryStatus,Status,CurrentVersion,DrawingSource) VALUES ($id,$name,$size,$timestamp,$type,$upload,$verification,$verificationError,$geometry,$status,$version,$source)";
        Add(command, "$id", document.DocumentId);
        Add(command, "$name", document.OriginalFileName);
        Add(command, "$size", document.FileSizeBytes);
        Add(command, "$timestamp", document.UploadTimestampUtc);
        Add(command, "$type", document.DocumentType);
        Add(command, "$upload", document.UploadStatus);
        Add(command, "$verification", document.VerificationStatus);
        command.Parameters.AddWithValue("$verificationError", (object?)document.VerificationError ?? DBNull.Value);
        Add(command, "$geometry", document.GeometryStatus);
        Add(command, "$status", document.Status);
        Add(command, "$version", document.CurrentVersion);
        Add(command, "$source", document.DrawingSource == DrawingSource.Unknown ? DrawingSource.Uploaded : document.DrawingSource);
        if (_documentHasUploaderColumns)
        {
            command.Parameters.AddWithValue("$uploaderId", (object?)document.UploaderUserId ?? DBNull.Value);
            command.Parameters.AddWithValue("$uploaderName", (object?)document.UploaderDisplayName ?? DBNull.Value);
        }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertVersionAsync(SqliteConnection connection, SqliteTransaction transaction, DocumentVersion version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO DocumentVersion (VersionId,DocumentId,VersionNumber,StoredRelativePath,FileSizeBytes,CreatedUtc,DrawingSource,FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion) VALUES ($id,$documentId,$number,$path,$size,$created,$source,$fingerprint,$algorithm,$canonicalization)";
        Add(command, "$id", version.VersionId);
        Add(command, "$documentId", version.DocumentId);
        Add(command, "$number", version.VersionNumber);
        Add(command, "$path", version.StoredRelativePath);
        Add(command, "$size", version.FileSizeBytes);
        Add(command, "$created", version.CreatedUtc);
        Add(command, "$source", version.DrawingSource == DrawingSource.Unknown ? DrawingSource.Uploaded : version.DrawingSource);
        command.Parameters.AddWithValue("$fingerprint", (object?)version.Fingerprint is null ? DBNull.Value : JsonSerializer.Serialize(version.Fingerprint));
        command.Parameters.AddWithValue("$algorithm", (object?)version.Fingerprint?.Algorithm ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonicalization", (object?)version.Fingerprint?.CanonicalizationVersion ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateCurrentVersionAsync(SqliteConnection connection, SqliteTransaction transaction, Guid documentId, DocumentVersion version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Document SET CurrentVersion=$number,FileSizeBytes=$size,UploadTimestampUtc=$timestamp WHERE DocumentId=$id";
        Add(command, "$number", version.VersionNumber);
        Add(command, "$size", version.FileSizeBytes);
        Add(command, "$timestamp", version.CreatedUtc);
        Add(command, "$id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAuditAsync(SqliteConnection connection, SqliteTransaction transaction, AuditEntry auditEntry, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,$documentId,$action,$userId,$timestamp,$details)";
        Add(command, "$id", auditEntry.AuditId);
        command.Parameters.AddWithValue("$documentId", (object?)auditEntry.DocumentId?.ToString() ?? DBNull.Value);
        Add(command, "$action", auditEntry.Action);
        command.Parameters.AddWithValue("$userId", (object?)auditEntry.UserId ?? DBNull.Value);
        Add(command, "$timestamp", auditEntry.TimestampUtc);
        Add(command, "$details", auditEntry.DetailsJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Document> MapAsync(SqliteDataReader reader, SqliteConnection connection, CancellationToken cancellationToken)
    {
        var document = new Document
        {
            DocumentId = Guid.Parse(reader.GetString(reader.GetOrdinal("DocumentId"))),
            OriginalFileName = reader.GetString(reader.GetOrdinal("OriginalFileName")),
            FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
            UploadTimestampUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("UploadTimestampUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DocumentType = (DrawVerify.Core.Enums.DocumentType)reader.GetInt32(reader.GetOrdinal("DocumentType")),
            UploadStatus = (DrawVerify.Core.Enums.UploadStatus)reader.GetInt32(reader.GetOrdinal("UploadStatus")),
            VerificationStatus = (DrawVerify.Core.Enums.VerificationStatus)reader.GetInt32(reader.GetOrdinal("VerificationStatus")),
            DrawingSource = (DrawVerify.Core.Enums.DrawingSource)reader.GetInt32(reader.GetOrdinal("DrawingSource")),
            VerificationError = NullableString(reader, "VerificationError"),
            GeometryStatus = (DrawVerify.Core.Enums.GeometryStatus)reader.GetInt32(reader.GetOrdinal("GeometryStatus")),
            Status = (DrawVerify.Core.Enums.DocumentStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            CurrentVersion = reader.GetInt32(reader.GetOrdinal("CurrentVersion")),
            UploaderUserId = _documentHasUploaderColumns ? NullableString(reader, "UploaderUserId") : null,
            UploaderDisplayName = _documentHasUploaderColumns ? NullableString(reader, "UploaderDisplayName") : null
        };

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VersionId,DocumentId,VersionNumber,StoredRelativePath,FileSizeBytes,CreatedUtc,DrawingSource,FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion FROM DocumentVersion WHERE DocumentId=$id ORDER BY VersionNumber";
        command.Parameters.AddWithValue("$id", document.DocumentId.ToString());
        await using var versionReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var versions = new List<DocumentVersion>();
        while (await versionReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var fingerprint = versionReader.IsDBNull(7) ? null : JsonSerializer.Deserialize<DocumentFingerprint>(versionReader.GetString(7));
            if (fingerprint is not null && (!versionReader.IsDBNull(8) || !versionReader.IsDBNull(9))) fingerprint = fingerprint with { Algorithm = versionReader.IsDBNull(8) ? fingerprint.Algorithm : versionReader.GetString(8), CanonicalizationVersion = versionReader.IsDBNull(9) ? fingerprint.CanonicalizationVersion : versionReader.GetInt32(9) };
            versions.Add(new DocumentVersion
            {
                VersionId = Guid.Parse(versionReader.GetString(0)),
                DocumentId = Guid.Parse(versionReader.GetString(1)),
                VersionNumber = versionReader.GetInt32(2),
                StoredRelativePath = versionReader.GetString(3),
                FileSizeBytes = versionReader.GetInt64(4),
                CreatedUtc = DateTime.Parse(versionReader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                ,DrawingSource = (DrawVerify.Core.Enums.DrawingSource)versionReader.GetInt32(6), Fingerprint = fingerprint
            });
        }

        return document with { Version = versions.LastOrDefault(), Versions = versions };
    }

    private static string? NullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string CreateDocumentSql(bool includeUploaderColumns) => includeUploaderColumns
        ? "CREATE TABLE Document (DocumentId TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, UploadTimestampUtc TEXT NOT NULL, DocumentType INTEGER NOT NULL, UploadStatus INTEGER NOT NULL, VerificationStatus INTEGER NOT NULL DEFAULT 0, VerificationError TEXT NULL, GeometryStatus INTEGER NOT NULL, Status INTEGER NOT NULL, CurrentVersion INTEGER NOT NULL, DrawingSource INTEGER NOT NULL DEFAULT 1, UploaderUserId TEXT NULL, UploaderDisplayName TEXT NULL)"
        : "CREATE TABLE Document (DocumentId TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, UploadTimestampUtc TEXT NOT NULL, DocumentType INTEGER NOT NULL, UploadStatus INTEGER NOT NULL, VerificationStatus INTEGER NOT NULL DEFAULT 0, VerificationError TEXT NULL, GeometryStatus INTEGER NOT NULL, Status INTEGER NOT NULL, CurrentVersion INTEGER NOT NULL, DrawingSource INTEGER NOT NULL DEFAULT 1)";

    private static string CreateDocumentVersionSql() => "CREATE TABLE DocumentVersion (VersionId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, VersionNumber INTEGER NOT NULL, StoredRelativePath TEXT NOT NULL, FileSizeBytes INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, DrawingSource INTEGER NOT NULL DEFAULT 1, FingerprintJson TEXT NULL, FingerprintAlgorithm TEXT NULL, FingerprintCanonicalizationVersion INTEGER NULL, FOREIGN KEY(DocumentId) REFERENCES Document(DocumentId))";
    private static string CreateAuditSql() => "CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL)";
    private static string CreateDimensionDataSql() => "CREATE TABLE DimensionData (DimensionDataId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, DocumentVersionId TEXT NOT NULL, SourceEntityOrdinal INTEGER NOT NULL, DimensionType INTEGER NOT NULL, NominalValue REAL NOT NULL, LowerTolerance REAL NULL, UpperTolerance REAL NULL, DisplayText TEXT NULL, RadialKind INTEGER NOT NULL DEFAULT 0, Layer TEXT NULL, PreviewAnchor TEXT NULL, BlockName TEXT NULL, ViewName TEXT NULL, LocationX REAL NULL, LocationY REAL NULL, FOREIGN KEY(DocumentId) REFERENCES Document(DocumentId), FOREIGN KEY(DocumentVersionId) REFERENCES DocumentVersion(VersionId), UNIQUE(DocumentVersionId,SourceEntityOrdinal))";

    private static void RebuildDocumentWithoutRemovedIdentityColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        bool sourceHasVerificationStatus)
    {
        var indexes = GetIndexDefinitions(connection, transaction, "Document");
        Execute(connection, transaction, "ALTER TABLE Document RENAME TO Document_legacy");
        Execute(connection, transaction, CreateDocumentSql(includeUploaderColumns: false));
        var verificationStatus = sourceHasVerificationStatus ? "VerificationStatus" : "0";
        var verificationError = GetColumns(connection, transaction, "Document_legacy").Contains("VerificationError") ? "VerificationError" : "NULL";
        Execute(connection, transaction, "INSERT INTO Document (" + DocumentColumns + ") SELECT DocumentId,OriginalFileName,FileSizeBytes,UploadTimestampUtc,DocumentType,UploadStatus," + verificationStatus + "," + verificationError + ",GeometryStatus,Status,CurrentVersion,1 FROM Document_legacy");
        Execute(connection, transaction, "DROP TABLE Document_legacy");
        foreach (var index in indexes)
            Execute(connection, transaction, index);
    }

    private static void RebuildDocumentVersionWithoutRemovedIdentityColumns(SqliteConnection connection, SqliteTransaction transaction)
    {
        var indexes = GetIndexDefinitions(connection, transaction, "DocumentVersion");
        var legacyColumns = GetColumns(connection, transaction, "DocumentVersion");
        Execute(connection, transaction, "ALTER TABLE DocumentVersion RENAME TO DocumentVersion_legacy");
        Execute(connection, transaction, CreateDocumentVersionSql());
        var fingerprintJson = legacyColumns.Contains("FingerprintJson") ? "FingerprintJson" : "NULL";
        var fingerprintAlgorithm = legacyColumns.Contains("FingerprintAlgorithm") ? "FingerprintAlgorithm" : "NULL";
        var fingerprintVersion = legacyColumns.Contains("FingerprintCanonicalizationVersion") ? "FingerprintCanonicalizationVersion" : "NULL";
        Execute(connection, transaction, $"INSERT INTO DocumentVersion (VersionId,DocumentId,VersionNumber,StoredRelativePath,FileSizeBytes,CreatedUtc,DrawingSource,FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion) SELECT VersionId,DocumentId,VersionNumber,StoredRelativePath,FileSizeBytes,CreatedUtc,1,{fingerprintJson},{fingerprintAlgorithm},{fingerprintVersion} FROM DocumentVersion_legacy");
        Execute(connection, transaction, "DROP TABLE DocumentVersion_legacy");
        foreach (var index in indexes)
            Execute(connection, transaction, index);
    }

    private static HashSet<string> GetColumns(SqliteConnection connection, SqliteTransaction transaction, string table)
        => GetColumnOrdinals(connection, transaction, table).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> GetColumnOrdinals(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            columns[reader.GetString(1)] = reader.GetInt32(0);
        return columns;
    }

    private static IReadOnlyList<string> GetIndexDefinitions(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name=$table AND sql IS NOT NULL";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        var definitions = new List<string>();
        while (reader.Read())
            definitions.Add(reader.GetString(0));
        return definitions;
    }

    private static void AddColumnIfMissing(SqliteConnection connection, SqliteTransaction transaction, string table, string column, string definition)
    {
        if (GetColumns(connection, transaction, table).Contains(column))
            return;
        Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private static bool Exists(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value switch
        {
            Guid id => id.ToString(),
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => value
        });
    }
}
