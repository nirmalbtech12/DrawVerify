using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteVerificationSessionRepository : IVerificationSessionRepository
{
    private const string GdtSelect = "SELECT GdtResultId,VerificationSessionId,Identifier,ControlType,Symbol,ToleranceValue,Modifiers,DatumReferences,SourceDocument,SourceDocumentVersion,SourceLocation,RawText,EntityHandle,Layer,BlockName,ViewName,Diagnostics,RuleVersion,ControlledFeature,NominalBasicLocation,ToleranceZoneType,ToleranceZoneDiameter,NormalizedModifiers,DatumScheme,ComparisonCriteria,SourcePairId,ComparisonDetails,HasCompositeFrame,Status,Reason,ReviewComment,RequiresEngineeringReview,ReviewedUtc,ReviewDisposition FROM GdtResult";
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
        command.CommandText = $"PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS VerificationSession (VerificationSessionId TEXT PRIMARY KEY, StepDocumentId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL, StepDocumentVersionId TEXT NULL, DxfDocumentVersionId TEXT NULL, Status INTEGER NOT NULL, ErrorMessage TEXT NULL, UpdatedUtc TEXT NOT NULL, ToleranceThresholdMm REAL NULL, UNIQUE(StepDocumentId,DxfDocumentId)); CREATE TABLE IF NOT EXISTS DimensionMatch (DimensionMatchId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, DxfDimensionReference INTEGER NOT NULL, StepDocumentVersionId TEXT NOT NULL, StepGeometryReference TEXT NULL, GeometryType TEXT NULL, ConfidenceScore REAL NULL, Confidence INTEGER NULL, RequiresManualReview INTEGER NOT NULL, Status INTEGER NOT NULL, ReasonCode TEXT NULL, DimensionType INTEGER NULL, NominalValue REAL NULL, DimensionText TEXT NULL, ReasonExplanation TEXT NULL, FOREIGN KEY(VerificationSessionId) REFERENCES VerificationSession(VerificationSessionId){versionForeignKeys}); CREATE TABLE IF NOT EXISTS GdtResult (GdtResultId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, Identifier TEXT NOT NULL, ControlType INTEGER NOT NULL, Symbol TEXT NULL, ToleranceValue TEXT NULL, Modifiers TEXT NULL, DatumReferences TEXT NOT NULL, SourceDocument TEXT NOT NULL, SourceDocumentVersion TEXT NULL, SourceLocation TEXT NULL, RawText TEXT NULL, EntityHandle TEXT NULL, Layer TEXT NULL, BlockName TEXT NULL, ViewName TEXT NULL, Diagnostics TEXT NOT NULL DEFAULT '[]', Status INTEGER NOT NULL, Reason TEXT NULL, ReviewComment TEXT NULL, RequiresEngineeringReview INTEGER NOT NULL DEFAULT 0, ReviewedUtc TEXT NULL, FOREIGN KEY(VerificationSessionId) REFERENCES VerificationSession(VerificationSessionId)); CREATE INDEX IF NOT EXISTS IX_DimensionMatch_SessionStatus ON DimensionMatch(VerificationSessionId,Status); CREATE INDEX IF NOT EXISTS IX_GdtResult_SessionStatus ON GdtResult(VerificationSessionId,Status);";
        command.ExecuteNonQuery();
        foreach (var column in new[] { ("DimensionType", "INTEGER"), ("NominalValue", "REAL"), ("DimensionText", "TEXT"), ("ReasonExplanation", "TEXT"), ("MeasuredValue", "REAL"), ("EffectiveLowerTolerance", "REAL"), ("EffectiveUpperTolerance", "REAL"), ("ToleranceUnit", "TEXT"), ("ToleranceSource", "INTEGER"), ("DxfLayer", "TEXT"), ("PreviewAnchor", "TEXT"), ("BlockName", "TEXT"), ("ViewName", "TEXT"), ("LocationX", "REAL"), ("LocationY", "REAL"), ("ReviewComment", "TEXT"), ("ResolutionStatus", "INTEGER"), ("RequiresDesignUpdate", "INTEGER") })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = $"ALTER TABLE DimensionMatch ADD COLUMN {column.Item1} {column.Item2}";
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        }
        foreach (var column in new[] { ("EntityHandle", "TEXT"), ("Layer", "TEXT"), ("BlockName", "TEXT"), ("ViewName", "TEXT"), ("Diagnostics", "TEXT NOT NULL DEFAULT '[]'"), ("RuleVersion", "TEXT NOT NULL DEFAULT '1.0.0'"), ("ControlledFeature", "TEXT"), ("NominalBasicLocation", "TEXT"), ("ToleranceZoneType", "TEXT"), ("ToleranceZoneDiameter", "TEXT"), ("NormalizedModifiers", "TEXT"), ("DatumScheme", "TEXT"), ("ComparisonCriteria", "TEXT"), ("SourcePairId", "TEXT"), ("ComparisonDetails", "TEXT"), ("HasCompositeFrame", "INTEGER NOT NULL DEFAULT 0"), ("ReviewDisposition", "TEXT") })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = $"ALTER TABLE GdtResult ADD COLUMN {column.Item1} {column.Item2}";
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
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT DimensionMatchId,VerificationSessionId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,GeometryType,ConfidenceScore,Confidence,RequiresManualReview,Status,ReasonCode,DimensionType,NominalValue,DimensionText,ReasonExplanation,MeasuredValue,EffectiveLowerTolerance,EffectiveUpperTolerance,ToleranceUnit,ToleranceSource,DxfLayer,PreviewAnchor,BlockName,ViewName,LocationX,LocationY,ReviewComment,ResolutionStatus,RequiresDesignUpdate FROM DimensionMatch WHERE VerificationSessionId=$id" + (status is null ? string.Empty : " AND Status=$status") + " ORDER BY DxfDimensionReference"; command.Parameters.AddWithValue("$id", sessionId.ToString()); if (status is not null) command.Parameters.AddWithValue("$status", status.Value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<DimensionMatch>(); while (await reader.ReadAsync(cancellationToken)) result.Add(MapMatch(reader)); return result;
    }
    public async Task<IReadOnlyList<GdtResult>> GetGdtResultsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = GdtSelect + " WHERE VerificationSessionId=$id ORDER BY rowid"; command.Parameters.AddWithValue("$id", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<GdtResult>(); while (await reader.ReadAsync(cancellationToken)) results.Add(MapGdt(reader)); return results;
    }
    public async Task ReplaceGdtResultsAsync(Guid sessionId, IReadOnlyCollection<GdtResult> results, string userId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Session and audit user are required.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try { var existing = await LoadGdtResultsAsync(connection, transaction, sessionId, cancellationToken); await Exec(connection, transaction, "DELETE FROM GdtResult WHERE VerificationSessionId=$session", cancellationToken, ("$session", sessionId.ToString())); foreach (var result in results) { if (string.IsNullOrWhiteSpace(result.RuleVersion)) throw new InvalidOperationException("GD&T results require a rule version."); if (!Enum.IsDefined(result.ControlType)) throw new InvalidOperationException("GD&T results require a valid control type."); if (!Enum.IsDefined(result.Status)) throw new InvalidOperationException("GD&T results require a valid status."); var carried = existing.FirstOrDefault(item => item.Identifier == result.Identifier && item.SourceDocument == result.SourceDocument && item.SourceDocumentVersion == result.SourceDocumentVersion); var review = MergeReviewFields(result, carried); await Exec(connection, transaction, "INSERT INTO GdtResult (GdtResultId,VerificationSessionId,Identifier,ControlType,Symbol,ToleranceValue,Modifiers,DatumReferences,SourceDocument,SourceDocumentVersion,SourceLocation,RawText,EntityHandle,Layer,BlockName,ViewName,Diagnostics,RuleVersion,ControlledFeature,NominalBasicLocation,ToleranceZoneType,ToleranceZoneDiameter,NormalizedModifiers,DatumScheme,ComparisonCriteria,SourcePairId,ComparisonDetails,HasCompositeFrame,Status,Reason,ReviewComment,RequiresEngineeringReview,ReviewedUtc,ReviewDisposition) VALUES ($id,$session,$identifier,$type,$symbol,$tolerance,$modifiers,$datums,$source,$version,$location,$raw,$handle,$layer,$block,$view,$diagnostics,$rule,$feature,$nominal,$zone,$diameter,$normalized,$scheme,$criteria,$pair,$details,$composite,$status,$reason,$comment,$engineering,$reviewed,$disposition)", cancellationToken, ("$id", result.GdtResultId.ToString()), ("$session", sessionId.ToString()), ("$identifier", result.Identifier), ("$type", result.ControlType), ("$symbol", (object?)result.Symbol ?? DBNull.Value), ("$tolerance", (object?)result.ToleranceValue ?? DBNull.Value), ("$modifiers", (object?)result.Modifiers ?? DBNull.Value), ("$datums", System.Text.Json.JsonSerializer.Serialize(result.DatumReferences)), ("$source", result.SourceDocument), ("$version", (object?)result.SourceDocumentVersion ?? DBNull.Value), ("$location", (object?)result.SourceLocation ?? DBNull.Value), ("$raw", (object?)result.RawText ?? DBNull.Value), ("$handle", (object?)result.EntityHandle ?? DBNull.Value), ("$layer", (object?)result.Layer ?? DBNull.Value), ("$block", (object?)result.BlockName ?? DBNull.Value), ("$view", (object?)result.ViewName ?? DBNull.Value), ("$diagnostics", System.Text.Json.JsonSerializer.Serialize(result.Diagnostics)), ("$rule", result.RuleVersion), ("$feature", (object?)result.ControlledFeature ?? DBNull.Value), ("$nominal", (object?)result.NominalBasicLocation ?? DBNull.Value), ("$zone", (object?)result.ToleranceZoneType ?? DBNull.Value), ("$diameter", (object?)result.ToleranceZoneDiameter ?? DBNull.Value), ("$normalized", (object?)result.NormalizedModifiers ?? DBNull.Value), ("$scheme", (object?)result.DatumScheme ?? DBNull.Value), ("$criteria", (object?)result.ComparisonCriteria ?? DBNull.Value), ("$pair", (object?)result.SourcePairId ?? DBNull.Value), ("$details", (object?)result.ComparisonDetails ?? DBNull.Value), ("$composite", result.HasCompositeFrame ? 1 : 0), ("$status", result.Status), ("$reason", (object?)result.Reason ?? DBNull.Value), ("$comment", (object?)review.ReviewComment ?? DBNull.Value), ("$engineering", review.RequiresEngineeringReview ? 1 : 0), ("$reviewed", (object?)review.ReviewedUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value), ("$disposition", (object?)review.ReviewDisposition ?? DBNull.Value)); await WriteGdtAuditAsync(connection, transaction, sessionId, result, DocumentAuditAction.GdtValidationCompleted, userId, null, review); } await transaction.CommitAsync(cancellationToken); } catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }
    public async Task UpdateGdtReviewAsync(Guid sessionId, Guid gdtResultId, string? comment, bool requiresEngineeringReview, string userId, CancellationToken cancellationToken = default)
    { if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Audit user is required.", nameof(userId)); comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(); await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken); try { var before = await LoadGdtAsync(connection, transaction, sessionId, gdtResultId, cancellationToken); if (before is null) throw new InvalidOperationException("The GD&T result was not found."); var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); var changed = await Exec(connection, transaction, "UPDATE GdtResult SET ReviewComment=$comment,RequiresEngineeringReview=$engineering,ReviewedUtc=$reviewed WHERE GdtResultId=$id AND VerificationSessionId=$session", cancellationToken, ("$comment", (object?)comment ?? DBNull.Value), ("$engineering", requiresEngineeringReview ? 1 : 0), ("$reviewed", now), ("$id", gdtResultId.ToString()), ("$session", sessionId.ToString())); if (changed != 1) throw new InvalidOperationException("The GD&T result was not found."); var after = before.WithReview(comment, requiresEngineeringReview, DateTimeOffset.UtcNow); var action = string.IsNullOrWhiteSpace(comment) ? DocumentAuditAction.GdtReviewChanged : DocumentAuditAction.CommentAdded; await WriteGdtAuditAsync(connection, transaction, sessionId, after, action, userId, before, after); await transaction.CommitAsync(cancellationToken); } catch { await transaction.RollbackAsync(CancellationToken.None); throw; } }
    public async Task UpdateGdtDispositionAsync(Guid sessionId, Guid gdtResultId, string disposition, string userId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || gdtResultId == Guid.Empty) throw new ArgumentException("Session and GD&T result IDs are required.");
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Audit user is required.", nameof(userId));
        var allowed = new[] { "Engineering review", "Drawing update", "Future scope" };
        if (!allowed.Contains(disposition, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("The GD&T disposition is invalid.", nameof(disposition));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = await LoadGdtAsync(connection, transaction, sessionId, gdtResultId, cancellationToken) ?? throw new InvalidOperationException("The GD&T result was not found.");
            if (before.Status != VerificationStatus.CannotValidate) throw new InvalidOperationException("Only Cannot Validate results may receive a disposition.");
            var canonical = allowed.First(item => string.Equals(item, disposition, StringComparison.OrdinalIgnoreCase));
            var after = before.WithDisposition(canonical);
            await Exec(connection, transaction, "UPDATE GdtResult SET ReviewDisposition=$disposition,ReviewedUtc=$reviewed WHERE GdtResultId=$id AND VerificationSessionId=$session", cancellationToken, ("$disposition", canonical), ("$reviewed", after.ReviewedUtc!.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)), ("$id", gdtResultId.ToString()), ("$session", sessionId.ToString()));
            await WriteGdtAuditAsync(connection, transaction, sessionId, after, DocumentAuditAction.DispositionChanged, userId, before, after);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
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
            var existingReviews = await LoadReviewsAsync(connection, transaction, session.VerificationSessionId, cancellationToken).ConfigureAwait(false);
            await Exec(connection, transaction, "DELETE FROM DimensionMatch WHERE VerificationSessionId=$id", cancellationToken, ("$id", session.VerificationSessionId.ToString()));
            foreach (var match in matches)
            {
                ValidateMatch(match, session);
                var review = match.Review;
                var reviewKey = match.Status is DimensionMatchStatus.ExtraDimension or DimensionMatchStatus.MissingDimension
                    ? (match.StepDocumentVersionId, match.StepGeometryReference ?? string.Empty)
                    : (match.DxfDocumentVersionId, match.DxfDimensionReference.ToString(CultureInfo.InvariantCulture));
                if (review is null && existingReviews.TryGetValue(reviewKey, out var existingReview))
                    review = existingReview with { DimensionMatchId = match.DimensionMatchId };
                await Exec(connection, transaction, "INSERT INTO DimensionMatch (DimensionMatchId,VerificationSessionId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,GeometryType,ConfidenceScore,Confidence,RequiresManualReview,Status,ReasonCode,DimensionType,NominalValue,DimensionText,ReasonExplanation,MeasuredValue,EffectiveLowerTolerance,EffectiveUpperTolerance,ToleranceUnit,ToleranceSource,DxfLayer,PreviewAnchor,BlockName,ViewName,LocationX,LocationY,ReviewComment,ResolutionStatus,RequiresDesignUpdate) VALUES ($match,$session,$dxf,$ordinal,$step,$reference,$type,$score,$confidence,$review,$status,$reason,$dimensionType,$nominal,$text,$explanation,$measured,$lower,$upper,$unit,$source,$layer,$anchor,$block,$view,$x,$y,$comment,$resolution,$designUpdate)", cancellationToken, ("$match", match.DimensionMatchId.ToString()), ("$session", match.VerificationSessionId.ToString()), ("$dxf", match.DxfDocumentVersionId.ToString()), ("$ordinal", match.DxfDimensionReference), ("$step", match.StepDocumentVersionId.ToString()), ("$reference", (object?)match.StepGeometryReference ?? DBNull.Value), ("$type", (object?)match.GeometryType ?? DBNull.Value), ("$score", (object?)match.ConfidenceScore ?? DBNull.Value), ("$confidence", (object?)match.Confidence ?? DBNull.Value), ("$review", match.RequiresManualReview ? 1 : 0), ("$status", match.Status), ("$reason", (object?)match.ReasonCode ?? DBNull.Value), ("$dimensionType", (object?)match.DimensionType ?? DBNull.Value), ("$nominal", (object?)match.NominalValue ?? DBNull.Value), ("$text", (object?)match.DimensionText ?? DBNull.Value), ("$explanation", (object?)match.ReasonExplanation ?? DBNull.Value), ("$measured", (object?)match.MeasuredValue ?? DBNull.Value), ("$lower", (object?)match.EffectiveLowerTolerance ?? DBNull.Value), ("$upper", (object?)match.EffectiveUpperTolerance ?? DBNull.Value), ("$unit", (object?)match.ToleranceUnit ?? DBNull.Value), ("$source", (object?)match.ToleranceSource ?? DBNull.Value), ("$layer", (object?)match.DxfLayer ?? DBNull.Value), ("$anchor", (object?)match.PreviewAnchor ?? DBNull.Value), ("$block", (object?)match.BlockName ?? DBNull.Value), ("$view", (object?)match.ViewName ?? DBNull.Value), ("$x", (object?)match.LocationX ?? DBNull.Value), ("$y", (object?)match.LocationY ?? DBNull.Value), ("$comment", (object?)review?.Comment ?? DBNull.Value), ("$resolution", review?.Status ?? DimensionResolutionStatus.Open), ("$designUpdate", review?.RequiresDesignUpdate == true ? 1 : 0));
            }
            var updated = await Exec(connection, transaction, "UPDATE VerificationSession SET StepDocumentVersionId=$step,DxfDocumentVersionId=$dxf,Status=$status,ErrorMessage=$error,UpdatedUtc=$updated,ToleranceThresholdMm=$tolerance WHERE VerificationSessionId=$id", cancellationToken, ("$step", (object?)session.StepDocumentVersionId?.ToString() ?? DBNull.Value), ("$dxf", (object?)session.DxfDocumentVersionId?.ToString() ?? DBNull.Value), ("$status", session.Status), ("$error", (object?)session.ErrorMessage ?? DBNull.Value), ("$updated", session.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$tolerance", (object?)session.ToleranceThresholdMm ?? DBNull.Value), ("$id", session.VerificationSessionId.ToString()));
            if (updated != 1) throw new InvalidOperationException("The verification session disappeared during result replacement.");
            await Exec(connection, transaction, "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,NULL,$action,$user,$time,$details)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$action", auditAction), ("$user", userId), ("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$details", detailsJson));
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }
    private static async Task<Dictionary<(Guid VersionId, string Reference), DimensionReview>> LoadReviewsAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT DimensionMatchId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,ReviewComment,ResolutionStatus,RequiresDesignUpdate,Status FROM DimensionMatch WHERE VerificationSessionId=$session";
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var reviews = new Dictionary<(Guid, string), DimensionReview>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var status = (DimensionMatchStatus)reader.GetInt32(8);
            var key = status is DimensionMatchStatus.ExtraDimension or DimensionMatchStatus.MissingDimension
                ? (Guid.Parse(reader.GetString(3)), reader.IsDBNull(4) ? string.Empty : reader.GetString(4))
                : (Guid.Parse(reader.GetString(1)), reader.GetInt32(2).ToString(CultureInfo.InvariantCulture));
            reviews[key] = new DimensionReview { DimensionMatchId = Guid.Parse(reader.GetString(0)), Comment = reader.IsDBNull(5) ? null : reader.GetString(5), Status = reader.IsDBNull(6) ? DimensionResolutionStatus.Open : (DimensionResolutionStatus)reader.GetInt32(6), RequiresDesignUpdate = !reader.IsDBNull(7) && reader.GetInt32(7) != 0 };
        }
        return reviews;
    }
    public async Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, CancellationToken cancellationToken = default)
        => await UpdateReviewAsync(sessionId, dimensionMatchId, review, string.Empty, DocumentAuditAction.VerificationReportGenerated, cancellationToken).ConfigureAwait(false);

    public async Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, string userId, DocumentAuditAction action, CancellationToken cancellationToken = default)
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
            var changed = await Exec(connection, transaction, "UPDATE DimensionMatch SET ReviewComment=$comment,ResolutionStatus=$status,RequiresDesignUpdate=$designUpdate WHERE DimensionMatchId=$match AND VerificationSessionId=$session AND Status IN ($unmatched,$extra,$legacy)", cancellationToken,
                ("$comment", (object?)review.Comment?.Trim() ?? DBNull.Value), ("$status", review.Status), ("$designUpdate", review.RequiresDesignUpdate ? 1 : 0), ("$match", dimensionMatchId.ToString()), ("$session", sessionId.ToString()), ("$unmatched", DimensionMatchStatus.Unmatched), ("$extra", DimensionMatchStatus.ExtraDimension), ("$legacy", DimensionMatchStatus.MissingDimension));
            if (changed != 1) throw new InvalidOperationException("The unmatched dimension was not found in the verification session.");
            if (!string.IsNullOrWhiteSpace(userId) && action is DocumentAuditAction.CommentAdded or DocumentAuditAction.StatusChanged)
            {
                var details = System.Text.Json.JsonSerializer.Serialize(new { dimensionMatchId, action = action.ToString(), username = userId, timestampUtc = DateTime.UtcNow });
                await Exec(connection, transaction, "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,NULL,$action,$user,$time,$details)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$action", action), ("$user", userId), ("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$details", details));
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }
    private static async Task<GdtResult?> LoadGdtAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, Guid resultId, CancellationToken token)
    { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = GdtSelect + " WHERE GdtResultId=$id AND VerificationSessionId=$session"; command.Parameters.AddWithValue("$id", resultId.ToString()); command.Parameters.AddWithValue("$session", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? MapGdt(reader) : null; }
    private static async Task<IReadOnlyList<GdtResult>> LoadGdtResultsAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, CancellationToken token)
    { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = GdtSelect + " WHERE VerificationSessionId=$session ORDER BY rowid"; command.Parameters.AddWithValue("$session", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(token); var results = new List<GdtResult>(); while (await reader.ReadAsync(token)) results.Add(MapGdt(reader)); return results; }
    private static async Task<(Guid StepDocumentId, Guid DxfDocumentId)> LoadGdtDocumentPairAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, CancellationToken token)
    { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT StepDocumentId,DxfDocumentId FROM VerificationSession WHERE VerificationSessionId=$session"; command.Parameters.AddWithValue("$session", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) throw new InvalidOperationException("The GD&T verification session was not found."); return (Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))); }
    private static async Task WriteGdtAuditAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, GdtResult result, DocumentAuditAction action, string userId, GdtResult? before, GdtResult? after)
    {
        var effectiveAction = action == DocumentAuditAction.GdtValidationCompleted && result.Status == VerificationStatus.CannotValidate
            ? DocumentAuditAction.FlaggedCannotValidate
            : action;
        // Keep the historical storage action for review mutations so existing audit queries remain compatible.
        // The canonical event is carried by actionType for new consumers.
        var storageAction = effectiveAction switch
        {
            // Keep the established SQLite action values stable for existing audit
            // queries. The canonical event remains available in actionType below.
            DocumentAuditAction.FlaggedCannotValidate => DocumentAuditAction.GdtValidationCompleted,
            DocumentAuditAction.CommentAdded or DocumentAuditAction.DispositionChanged => DocumentAuditAction.GdtReviewChanged,
            _ => effectiveAction
        };
        var timestampUtc = DateTime.UtcNow;
        await Exec(connection, transaction, "UPDATE GdtResult SET ReviewDisposition=$disposition WHERE GdtResultId=$id AND VerificationSessionId=$session", CancellationToken.None, ("$disposition", (object?)(after?.ReviewDisposition ?? result.ReviewDisposition) ?? DBNull.Value), ("$id", result.GdtResultId.ToString()), ("$session", sessionId.ToString()));
        var documentPair = await LoadGdtDocumentPairAsync(connection, transaction, sessionId, CancellationToken.None);
        var details = System.Text.Json.JsonSerializer.Serialize(new
        {
            verificationSessionId = sessionId,
            stepDocumentId = documentPair.StepDocumentId,
            dxfDocumentId = documentPair.DxfDocumentId,
            annotationIdentifier = result.Identifier,
            controlIdentifier = result.Identifier,
            sourceDocument = result.SourceDocument,
            sourceDocumentVersion = result.SourceDocumentVersion,
            sourceLocation = result.SourceLocation,
            status = result.Status.ToString(),
            reason = result.Reason,
            actor = userId,
            timestampUtc,
            ruleVersion = result.RuleVersion,
            action = storageAction.ToString(),
            actionType = effectiveAction.ToString(),
            before = before is null ? null : new { comment = before.ReviewComment, requiresEngineeringReview = before.RequiresEngineeringReview, disposition = before.ReviewDisposition, reviewedUtc = before.ReviewedUtc, status = before.Status.ToString() },
            after = after is null ? new { comment = result.ReviewComment, requiresEngineeringReview = result.RequiresEngineeringReview, disposition = result.ReviewDisposition, reviewedUtc = result.ReviewedUtc, status = result.Status.ToString() } : new { comment = after.ReviewComment, requiresEngineeringReview = after.RequiresEngineeringReview, disposition = after.ReviewDisposition, reviewedUtc = after.ReviewedUtc, status = after.Status.ToString() }
        });
        await Exec(connection, transaction, "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,$document,$action,$user,$timestamp,$details)", CancellationToken.None, ("$id", Guid.NewGuid().ToString()), ("$document", sessionId.ToString()), ("$action", storageAction), ("$user", userId), ("$timestamp", timestampUtc.ToString("O", CultureInfo.InvariantCulture)), ("$details", details));
    }
    private static async Task<int> Exec(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
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
        if (match.VerificationSessionId != session.VerificationSessionId || match.DxfDocumentVersionId == Guid.Empty || match.StepDocumentVersionId == Guid.Empty || match.StepDocumentVersionId != session.StepDocumentVersionId || match.DxfDocumentVersionId != session.DxfDocumentVersionId || !Enum.IsDefined(match.Status) || (match.Confidence is not null && !Enum.IsDefined(match.Confidence.Value)) || (match.ConfidenceScore is not null && (!double.IsFinite(match.ConfidenceScore.Value) || match.ConfidenceScore is < 0 or > 1))) throw new InvalidOperationException("Invalid dimension match reference, status, confidence, score, or current document version.");
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
            case DimensionMatchStatus.ExtraDimension when match.DxfDimensionReference != -1 || string.IsNullOrWhiteSpace(match.StepGeometryReference):
                throw new InvalidOperationException("Extra-dimension results require a geometry reference and no DXF reference.");
        }
    }
    private static VerificationSession MapSession(SqliteDataReader r) => new() { VerificationSessionId = Guid.Parse(r.GetString(0)), StepDocumentId = Guid.Parse(r.GetString(1)), DxfDocumentId = Guid.Parse(r.GetString(2)), StepDocumentVersionId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), DxfDocumentVersionId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)), Status = (VerificationSessionStatus)r.GetInt32(5), ErrorMessage = r.IsDBNull(6) ? null : r.GetString(6), UpdatedUtc = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), ToleranceThresholdMm = r.IsDBNull(8) ? null : r.GetDouble(8) };
    private static DimensionMatch MapMatch(SqliteDataReader r) => new() { DimensionMatchId = Guid.Parse(r.GetString(0)), VerificationSessionId = Guid.Parse(r.GetString(1)), DxfDocumentVersionId = Guid.Parse(r.GetString(2)), DxfDimensionReference = r.GetInt32(3), StepDocumentVersionId = Guid.Parse(r.GetString(4)), StepGeometryReference = r.IsDBNull(5) ? null : r.GetString(5), GeometryType = r.IsDBNull(6) ? null : r.GetString(6), ConfidenceScore = r.IsDBNull(7) ? null : r.GetDouble(7), Confidence = r.IsDBNull(8) ? null : (MatchConfidence)r.GetInt32(8), RequiresManualReview = r.GetInt32(9) != 0, Status = (DimensionMatchStatus)r.GetInt32(10), ReasonCode = r.IsDBNull(11) ? null : r.GetString(11), DimensionType = r.IsDBNull(12) ? null : (DimensionType)r.GetInt32(12), NominalValue = r.IsDBNull(13) ? null : r.GetDouble(13), DimensionText = r.IsDBNull(14) ? null : r.GetString(14), ReasonExplanation = r.IsDBNull(15) ? null : r.GetString(15), MeasuredValue = r.IsDBNull(16) ? null : r.GetDouble(16), EffectiveLowerTolerance = r.IsDBNull(17) ? null : r.GetDouble(17), EffectiveUpperTolerance = r.IsDBNull(18) ? null : r.GetDouble(18), ToleranceUnit = r.IsDBNull(19) ? null : r.GetString(19), ToleranceSource = r.IsDBNull(20) ? null : (ToleranceSource)r.GetInt32(20), DxfLayer = r.IsDBNull(21) ? null : r.GetString(21), PreviewAnchor = r.IsDBNull(22) ? null : r.GetString(22), BlockName = r.IsDBNull(23) ? null : r.GetString(23), ViewName = r.IsDBNull(24) ? null : r.GetString(24), LocationX = r.IsDBNull(25) ? null : r.GetDouble(25), LocationY = r.IsDBNull(26) ? null : r.GetDouble(26), Review = new DimensionReview { DimensionMatchId = Guid.Parse(r.GetString(0)), Comment = r.IsDBNull(27) ? null : r.GetString(27), Status = r.IsDBNull(28) ? DimensionResolutionStatus.Open : (DimensionResolutionStatus)r.GetInt32(28), RequiresDesignUpdate = !r.IsDBNull(29) && r.GetInt32(29) != 0 } };
    private static GdtResult MergeReviewFields(GdtResult incoming, GdtResult? existing)
    {
        if (existing is null) return incoming;
        var hasStoredReview = existing.ReviewComment is not null || existing.RequiresEngineeringReview || existing.ReviewedUtc is not null || existing.ReviewDisposition is not null;
        if (!hasStoredReview) return incoming;
        var requiresEngineeringReview = incoming.ReviewedUtc is not null
            ? incoming.RequiresEngineeringReview
            : incoming.RequiresEngineeringReview || existing.RequiresEngineeringReview;
        var reviewSource = incoming.ReviewDisposition is null && existing.ReviewDisposition is not null
            ? incoming.WithDisposition(existing.ReviewDisposition)
            : incoming;
        return reviewSource.WithReview(reviewSource.ReviewComment ?? existing.ReviewComment, requiresEngineeringReview, incoming.ReviewedUtc ?? existing.ReviewedUtc);
    }

    private static T ReadEnum<T>(SqliteDataReader reader, int ordinal, string fieldName) where T : struct, Enum
    {
        var value = reader.GetInt32(ordinal);
        if (!Enum.IsDefined(typeof(T), value))
            throw new InvalidDataException($"Persisted GD&T {fieldName} value '{value}' is invalid.");
        return (T)Enum.ToObject(typeof(T), value);
    }

    private static string[] ReadJsonArray(SqliteDataReader reader, int ordinal, string fieldName)
    {
        if (reader.IsDBNull(ordinal)) return Array.Empty<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(ordinal)) ?? Array.Empty<string>(); }
        catch (System.Text.Json.JsonException exception) { throw new InvalidDataException($"Persisted GD&T {fieldName} JSON is invalid.", exception); }
    }

    private static GdtResult MapGdt(SqliteDataReader r)
    {
        var controlType = ReadEnum<GdtControlType>(r, 3, "control type");
        var status = ReadEnum<VerificationStatus>(r, 28, "status");
        var datumReferences = ReadJsonArray(r, 7, "datum references");
        var diagnostics = ReadJsonArray(r, 16, "diagnostics");
        return new GdtResult
        {
        GdtResultId = Guid.Parse(r.GetString(0)), VerificationSessionId = Guid.Parse(r.GetString(1)), Identifier = r.GetString(2), ControlType = controlType, Symbol = r.IsDBNull(4) ? null : r.GetString(4), ToleranceValue = r.IsDBNull(5) ? null : r.GetString(5), Modifiers = r.IsDBNull(6) ? null : r.GetString(6), DatumReferences = datumReferences, SourceDocument = r.GetString(8), SourceDocumentVersion = r.IsDBNull(9) ? null : r.GetString(9), SourceLocation = r.IsDBNull(10) ? null : r.GetString(10), RawText = r.IsDBNull(11) ? null : r.GetString(11), EntityHandle = r.IsDBNull(12) ? null : r.GetString(12), Layer = r.IsDBNull(13) ? null : r.GetString(13), BlockName = r.IsDBNull(14) ? null : r.GetString(14), ViewName = r.IsDBNull(15) ? null : r.GetString(15), Diagnostics = diagnostics, RuleVersion = r.IsDBNull(17) ? GdtResult.CurrentRuleVersion : r.GetString(17), ControlledFeature = r.IsDBNull(18) ? null : r.GetString(18), NominalBasicLocation = r.IsDBNull(19) ? null : r.GetString(19), ToleranceZoneType = r.IsDBNull(20) ? null : r.GetString(20), ToleranceZoneDiameter = r.IsDBNull(21) ? null : r.GetString(21), NormalizedModifiers = r.IsDBNull(22) ? null : r.GetString(22), DatumScheme = r.IsDBNull(23) ? null : r.GetString(23), ComparisonCriteria = r.IsDBNull(24) ? null : r.GetString(24), SourcePairId = r.IsDBNull(25) ? null : r.GetString(25), ComparisonDetails = r.IsDBNull(26) ? null : r.GetString(26), HasCompositeFrame = !r.IsDBNull(27) && r.GetInt32(27) != 0, Status = status, Reason = r.IsDBNull(29) ? null : r.GetString(29), ReviewComment = r.IsDBNull(30) ? null : r.GetString(30), RequiresEngineeringReview = !r.IsDBNull(31) && r.GetInt32(31) != 0, ReviewedUtc = r.IsDBNull(32) ? null : DateTimeOffset.Parse(r.GetString(32), CultureInfo.InvariantCulture), ReviewDisposition = r.IsDBNull(33) ? null : r.GetString(33)
        };
    }
}
