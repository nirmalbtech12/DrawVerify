using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteVerificationSessionRepository : IVerificationSessionRepository
{
    private readonly string _connectionString;
    public SqliteVerificationSessionRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        var hasDocumentVersionSchema = HasTable(connection, "Document") && HasTable(connection, "DocumentVersion");
        using var command = connection.CreateCommand();
        var versionForeignKeys = hasDocumentVersionSchema
            ? ", FOREIGN KEY(DxfDocumentVersionId) REFERENCES DocumentVersion(VersionId), FOREIGN KEY(StepDocumentVersionId) REFERENCES DocumentVersion(VersionId)"
            : string.Empty;
        command.CommandText = $"PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS VerificationSession (VerificationSessionId TEXT PRIMARY KEY, StepDocumentId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL, StepDocumentVersionId TEXT NULL, DxfDocumentVersionId TEXT NULL, Status INTEGER NOT NULL, ErrorMessage TEXT NULL, UpdatedUtc TEXT NOT NULL, ToleranceThresholdMm REAL NULL, UNIQUE(StepDocumentId,DxfDocumentId)); CREATE TABLE IF NOT EXISTS DimensionMatch (DimensionMatchId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, DxfDimensionReference INTEGER NOT NULL, StepDocumentVersionId TEXT NOT NULL, StepGeometryReference TEXT NULL, GeometryType TEXT NULL, ConfidenceScore REAL NULL, Confidence INTEGER NULL, RequiresManualReview INTEGER NOT NULL, Status INTEGER NOT NULL, ReasonCode TEXT NULL, DimensionType INTEGER NULL, NominalValue REAL NULL, DimensionText TEXT NULL, ReasonExplanation TEXT NULL, FOREIGN KEY(VerificationSessionId) REFERENCES VerificationSession(VerificationSessionId){versionForeignKeys}); CREATE INDEX IF NOT EXISTS IX_DimensionMatch_SessionStatus ON DimensionMatch(VerificationSessionId,Status);";
        command.ExecuteNonQuery();
        foreach (var column in new[] { ("DimensionType", "INTEGER"), ("NominalValue", "REAL"), ("DimensionText", "TEXT"), ("ReasonExplanation", "TEXT"), ("MeasuredValue", "REAL"), ("EffectiveLowerTolerance", "REAL"), ("EffectiveUpperTolerance", "REAL"), ("ToleranceUnit", "TEXT"), ("ToleranceSource", "INTEGER"), ("DxfLayer", "TEXT"), ("PreviewAnchor", "TEXT"), ("ReviewComment", "TEXT"), ("ResolutionStatus", "INTEGER") })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = $"ALTER TABLE DimensionMatch ADD COLUMN {column.Item1} {column.Item2}";
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        }
        using var sessionMigration = connection.CreateCommand(); sessionMigration.CommandText = "ALTER TABLE VerificationSession ADD COLUMN ToleranceThresholdMm REAL"; try { sessionMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
    }
    public async Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default)
    {
        if (stepDocumentId == Guid.Empty || dxfDocumentId == Guid.Empty) throw new ArgumentException("Document IDs are required.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO VerificationSession (VerificationSessionId,StepDocumentId,DxfDocumentId,Status,UpdatedUtc) VALUES ($id,$step,$dxf,0,$updated) ON CONFLICT(StepDocumentId,DxfDocumentId) DO NOTHING; SELECT VerificationSessionId,StepDocumentId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,Status,ErrorMessage,UpdatedUtc,ToleranceThresholdMm FROM VerificationSession WHERE StepDocumentId=$step AND DxfDocumentId=$dxf;";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$step", stepDocumentId.ToString()); command.Parameters.AddWithValue("$dxf", dxfDocumentId.ToString()); command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); return MapSession(reader);
    }
    public async Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT VerificationSessionId,StepDocumentId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,Status,ErrorMessage,UpdatedUtc,ToleranceThresholdMm FROM VerificationSession WHERE VerificationSessionId=$id"; command.Parameters.AddWithValue("$id", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }
    public async Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT DimensionMatchId,VerificationSessionId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,GeometryType,ConfidenceScore,Confidence,RequiresManualReview,Status,ReasonCode,DimensionType,NominalValue,DimensionText,ReasonExplanation,MeasuredValue,EffectiveLowerTolerance,EffectiveUpperTolerance,ToleranceUnit,ToleranceSource,DxfLayer,PreviewAnchor,ReviewComment,ResolutionStatus FROM DimensionMatch WHERE VerificationSessionId=$id" + (status is null ? string.Empty : " AND Status=$status") + " ORDER BY DxfDimensionReference"; command.Parameters.AddWithValue("$id", sessionId.ToString()); if (status is not null) command.Parameters.AddWithValue("$status", status.Value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<DimensionMatch>(); while (await reader.ReadAsync(cancellationToken)) result.Add(MapMatch(reader)); return result;
    }
    public async Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(matches); if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Audit user is required.", nameof(userId));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateSessionAndCurrentVersionsAsync(connection, transaction, session, auditAction == DocumentAuditAction.DimensionMatchingFailed, cancellationToken).ConfigureAwait(false);
            if (auditAction == DocumentAuditAction.DimensionMatchingFailed && matches.Count != 0)
                throw new InvalidOperationException("Failure audits cannot include dimension match rows.");
            var existingReviews = await LoadUnmatchedReviewsAsync(connection, transaction, session.VerificationSessionId, cancellationToken).ConfigureAwait(false);
            await Exec(connection, transaction, "DELETE FROM DimensionMatch WHERE VerificationSessionId=$id", cancellationToken, ("$id", session.VerificationSessionId.ToString()));
            foreach (var match in matches)
            {
                ValidateMatch(match, session);
                var review = match.Review;
                if (review is null && match.Status == DimensionMatchStatus.Unmatched && existingReviews.TryGetValue((match.DxfDocumentVersionId, match.DxfDimensionReference), out var existingReview))
                    review = existingReview with { DimensionMatchId = match.DimensionMatchId };
                await Exec(connection, transaction, "INSERT INTO DimensionMatch (DimensionMatchId,VerificationSessionId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,GeometryType,ConfidenceScore,Confidence,RequiresManualReview,Status,ReasonCode,DimensionType,NominalValue,DimensionText,ReasonExplanation,MeasuredValue,EffectiveLowerTolerance,EffectiveUpperTolerance,ToleranceUnit,ToleranceSource,DxfLayer,PreviewAnchor,ReviewComment,ResolutionStatus) VALUES ($match,$session,$dxf,$ordinal,$step,$reference,$type,$score,$confidence,$review,$status,$reason,$dimensionType,$nominal,$text,$explanation,$measured,$lower,$upper,$unit,$source,$layer,$anchor,$comment,$resolution)", cancellationToken, ("$match", match.DimensionMatchId.ToString()), ("$session", match.VerificationSessionId.ToString()), ("$dxf", match.DxfDocumentVersionId.ToString()), ("$ordinal", match.DxfDimensionReference), ("$step", match.StepDocumentVersionId.ToString()), ("$reference", (object?)match.StepGeometryReference ?? DBNull.Value), ("$type", (object?)match.GeometryType ?? DBNull.Value), ("$score", (object?)match.ConfidenceScore ?? DBNull.Value), ("$confidence", (object?)match.Confidence ?? DBNull.Value), ("$review", match.RequiresManualReview ? 1 : 0), ("$status", match.Status), ("$reason", (object?)match.ReasonCode ?? DBNull.Value), ("$dimensionType", (object?)match.DimensionType ?? DBNull.Value), ("$nominal", (object?)match.NominalValue ?? DBNull.Value), ("$text", (object?)match.DimensionText ?? DBNull.Value), ("$explanation", (object?)match.ReasonExplanation ?? DBNull.Value), ("$measured", (object?)match.MeasuredValue ?? DBNull.Value), ("$lower", (object?)match.EffectiveLowerTolerance ?? DBNull.Value), ("$upper", (object?)match.EffectiveUpperTolerance ?? DBNull.Value), ("$unit", (object?)match.ToleranceUnit ?? DBNull.Value), ("$source", (object?)match.ToleranceSource ?? DBNull.Value), ("$layer", (object?)match.DxfLayer ?? DBNull.Value), ("$anchor", (object?)match.PreviewAnchor ?? DBNull.Value), ("$comment", (object?)review?.Comment ?? DBNull.Value), ("$resolution", review?.Status ?? DimensionResolutionStatus.Open));
            }
            var updated = await Exec(connection, transaction, "UPDATE VerificationSession SET StepDocumentVersionId=$step,DxfDocumentVersionId=$dxf,Status=$status,ErrorMessage=$error,UpdatedUtc=$updated,ToleranceThresholdMm=$tolerance WHERE VerificationSessionId=$id", cancellationToken, ("$step", (object?)session.StepDocumentVersionId?.ToString() ?? DBNull.Value), ("$dxf", (object?)session.DxfDocumentVersionId?.ToString() ?? DBNull.Value), ("$status", session.Status), ("$error", (object?)session.ErrorMessage ?? DBNull.Value), ("$updated", session.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$tolerance", (object?)session.ToleranceThresholdMm ?? DBNull.Value), ("$id", session.VerificationSessionId.ToString()));
            if (updated != 1) throw new InvalidOperationException("The verification session disappeared during result replacement.");
            await Exec(connection, transaction, "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,NULL,$action,$user,$time,$details)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$action", auditAction), ("$user", userId), ("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$details", detailsJson));
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }
    private static async Task<Dictionary<(Guid VersionId, int Reference), DimensionReview>> LoadUnmatchedReviewsAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT DimensionMatchId,DxfDocumentVersionId,DxfDimensionReference,ReviewComment,ResolutionStatus FROM DimensionMatch WHERE VerificationSessionId=$session AND Status=$unmatched";
        command.Parameters.AddWithValue("$session", sessionId.ToString()); command.Parameters.AddWithValue("$unmatched", DimensionMatchStatus.Unmatched);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var reviews = new Dictionary<(Guid, int), DimensionReview>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            reviews[(Guid.Parse(reader.GetString(1)), reader.GetInt32(2))] = new DimensionReview { DimensionMatchId = Guid.Parse(reader.GetString(0)), Comment = reader.IsDBNull(3) ? null : reader.GetString(3), Status = reader.IsDBNull(4) ? DimensionResolutionStatus.Open : (DimensionResolutionStatus)reader.GetInt32(4) };
        }
        return reviews;
    }
    public async Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || dimensionMatchId == Guid.Empty) throw new ArgumentException("Session and dimension match IDs are required.");
        ArgumentNullException.ThrowIfNull(review);
        if (review.DimensionMatchId != dimensionMatchId) throw new ArgumentException("Review does not reference the requested dimension match.", nameof(review));
        if (!Enum.IsDefined(review.Status)) throw new ArgumentOutOfRangeException(nameof(review));
        if (review.Comment is not null && string.IsNullOrWhiteSpace(review.Comment)) throw new ArgumentException("A dimension comment cannot be empty.", nameof(review));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = await Exec(connection, transaction, "UPDATE DimensionMatch SET ReviewComment=$comment,ResolutionStatus=$status WHERE DimensionMatchId=$match AND VerificationSessionId=$session AND Status=$unmatched", cancellationToken,
                ("$comment", (object?)review.Comment?.Trim() ?? DBNull.Value), ("$status", review.Status), ("$match", dimensionMatchId.ToString()), ("$session", sessionId.ToString()), ("$unmatched", DimensionMatchStatus.Unmatched));
            if (changed != 1) throw new InvalidOperationException("The unmatched dimension was not found in the verification session.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }
    private static async Task<int> Exec(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static async Task ValidateSessionAndCurrentVersionsAsync(SqliteConnection connection, SqliteTransaction transaction, VerificationSession session, bool allowPreconditionFailure, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        var hasDocumentSchema = await HasTableAsync(connection, transaction, "Document", token).ConfigureAwait(false);
        var hasVersionSchema = await HasTableAsync(connection, transaction, "DocumentVersion", token).ConfigureAwait(false);
        var hasBothVersions = session.StepDocumentVersionId is not null && session.DxfDocumentVersionId is not null;
        if (!hasBothVersions && !allowPreconditionFailure)
            throw new InvalidOperationException("Matching results require current document versions.");
        command.CommandText = hasBothVersions && hasDocumentSchema && hasVersionSchema
            ? "SELECT COUNT(*) FROM VerificationSession s INNER JOIN Document sd ON sd.DocumentId=s.StepDocumentId INNER JOIN Document dd ON dd.DocumentId=s.DxfDocumentId INNER JOIN DocumentVersion sv ON sv.VersionId=$stepVersion AND sv.DocumentId=s.StepDocumentId AND sv.VersionNumber=sd.CurrentVersion INNER JOIN DocumentVersion dv ON dv.VersionId=$dxfVersion AND dv.DocumentId=s.DxfDocumentId AND dv.VersionNumber=dd.CurrentVersion WHERE s.VerificationSessionId=$session AND s.StepDocumentId=$stepDocument AND s.DxfDocumentId=$dxfDocument"
            : "SELECT COUNT(*) FROM VerificationSession WHERE VerificationSessionId=$session AND StepDocumentId=$stepDocument AND DxfDocumentId=$dxfDocument";
        command.Parameters.AddWithValue("$session", session.VerificationSessionId.ToString()); command.Parameters.AddWithValue("$stepDocument", session.StepDocumentId.ToString()); command.Parameters.AddWithValue("$dxfDocument", session.DxfDocumentId.ToString());
        if (hasBothVersions && hasDocumentSchema && hasVersionSchema) { command.Parameters.AddWithValue("$stepVersion", session.StepDocumentVersionId!.Value.ToString()); command.Parameters.AddWithValue("$dxfVersion", session.DxfDocumentVersionId!.Value.ToString()); }
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("The verification session or its current document versions are invalid.");
    }
    private static bool HasTable(SqliteConnection connection, string table) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name"; command.Parameters.AddWithValue("$name", table); return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1; }
    private static async Task<bool> HasTableAsync(SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken token) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name"; command.Parameters.AddWithValue("$name", table); return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1; }
    private static void ValidateMatch(DimensionMatch match, VerificationSession session)
    {
        if (match.VerificationSessionId != session.VerificationSessionId || match.DxfDocumentVersionId == Guid.Empty || match.StepDocumentVersionId == Guid.Empty || !Enum.IsDefined(match.Status) || (match.Confidence is not null && !Enum.IsDefined(match.Confidence.Value)) || (match.ConfidenceScore is not null && (!double.IsFinite(match.ConfidenceScore.Value) || match.ConfidenceScore is < 0 or > 1))) throw new InvalidOperationException("Invalid dimension match reference, status, confidence, or score.");
        switch (match.Status)
        {
            case DimensionMatchStatus.Matched when match.DxfDimensionReference < 0 || string.IsNullOrWhiteSpace(match.StepGeometryReference) || match.Confidence is null || match.ConfidenceScore is null:
                throw new InvalidOperationException("Matched results require dimension and geometry references plus confidence.");
            case DimensionMatchStatus.Unmatched when match.DxfDimensionReference < 0 || !string.IsNullOrWhiteSpace(match.StepGeometryReference) || match.Confidence is not null || match.ConfidenceScore is not null || string.IsNullOrWhiteSpace(match.ReasonCode):
                throw new InvalidOperationException("Unmatched results require a reason and no geometry association.");
            case DimensionMatchStatus.Mismatched when match.DxfDimensionReference < 0 || string.IsNullOrWhiteSpace(match.StepGeometryReference) || match.Confidence is null || match.ConfidenceScore is null || string.IsNullOrWhiteSpace(match.ReasonCode):
                throw new InvalidOperationException("Mismatched results require a dimension, geometry, confidence, and reason.");
            case DimensionMatchStatus.MissingDimension when match.DxfDimensionReference != -1 || string.IsNullOrWhiteSpace(match.StepGeometryReference):
                throw new InvalidOperationException("Missing-dimension results require a geometry reference and no DXF reference.");
        }
    }
    private static VerificationSession MapSession(SqliteDataReader r) => new() { VerificationSessionId = Guid.Parse(r.GetString(0)), StepDocumentId = Guid.Parse(r.GetString(1)), DxfDocumentId = Guid.Parse(r.GetString(2)), StepDocumentVersionId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), DxfDocumentVersionId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)), Status = (VerificationSessionStatus)r.GetInt32(5), ErrorMessage = r.IsDBNull(6) ? null : r.GetString(6), UpdatedUtc = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), ToleranceThresholdMm = r.IsDBNull(8) ? null : r.GetDouble(8) };
    private static DimensionMatch MapMatch(SqliteDataReader r) => new() { DimensionMatchId = Guid.Parse(r.GetString(0)), VerificationSessionId = Guid.Parse(r.GetString(1)), DxfDocumentVersionId = Guid.Parse(r.GetString(2)), DxfDimensionReference = r.GetInt32(3), StepDocumentVersionId = Guid.Parse(r.GetString(4)), StepGeometryReference = r.IsDBNull(5) ? null : r.GetString(5), GeometryType = r.IsDBNull(6) ? null : r.GetString(6), ConfidenceScore = r.IsDBNull(7) ? null : r.GetDouble(7), Confidence = r.IsDBNull(8) ? null : (MatchConfidence)r.GetInt32(8), RequiresManualReview = r.GetInt32(9) != 0, Status = (DimensionMatchStatus)r.GetInt32(10), ReasonCode = r.IsDBNull(11) ? null : r.GetString(11), DimensionType = r.IsDBNull(12) ? null : (DimensionType)r.GetInt32(12), NominalValue = r.IsDBNull(13) ? null : r.GetDouble(13), DimensionText = r.IsDBNull(14) ? null : r.GetString(14), ReasonExplanation = r.IsDBNull(15) ? null : r.GetString(15), MeasuredValue = r.IsDBNull(16) ? null : r.GetDouble(16), EffectiveLowerTolerance = r.IsDBNull(17) ? null : r.GetDouble(17), EffectiveUpperTolerance = r.IsDBNull(18) ? null : r.GetDouble(18), ToleranceUnit = r.IsDBNull(19) ? null : r.GetString(19), ToleranceSource = r.IsDBNull(20) ? null : (ToleranceSource)r.GetInt32(20), DxfLayer = r.IsDBNull(21) ? null : r.GetString(21), PreviewAnchor = r.IsDBNull(22) ? null : r.GetString(22), Review = new DimensionReview { DimensionMatchId = Guid.Parse(r.GetString(0)), Comment = r.IsDBNull(23) ? null : r.GetString(23), Status = r.IsDBNull(24) ? DimensionResolutionStatus.Open : (DimensionResolutionStatus)r.GetInt32(24) } };
}
