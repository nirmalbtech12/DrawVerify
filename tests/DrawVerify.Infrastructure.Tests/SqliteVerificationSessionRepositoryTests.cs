using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteVerificationSessionRepositoryTests
{
    [Fact(DisplayName = "TD-INF-EXTRA-001 round-trips extra metadata and completion audit")]
    public async Task Extra_row_survives_reload_and_recomparison_with_review_state()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-extra-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid();
            await CreateCurrentDocumentsAsync(path, step, dxf, stepVersion, dxfVersion);
            var repository = new SqliteVerificationSessionRepository(path); var session = await repository.GetOrCreateAsync(step, dxf) with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched };
            var id = Guid.NewGuid(); var extra = new DimensionMatch { DimensionMatchId = id, VerificationSessionId = session.VerificationSessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, DxfDimensionReference = -1, StepGeometryReference = "Face:extra", GeometryType = "Hole", Status = DimensionMatchStatus.ExtraDimension, MeasuredValue = 24, EffectiveLowerTolerance = -.2, EffectiveUpperTolerance = .1, LocationX = 3, LocationY = 4, Review = new DimensionReview { DimensionMatchId = id, Comment = "Should be shown", RequiresDesignUpdate = true } };
            var details = JsonSerializer.Serialize(new { stepDocumentId = step, dxfDocumentId = dxf, extraStepCount = 1, timestampUtc = DateTime.UtcNow, triggeredBy = "checker" });
            await repository.ReplaceResultsAsync(session, new[] { extra }, DocumentAuditAction.ToleranceComparisonApplied, "checker", details);
            var loaded = Assert.Single(await new SqliteVerificationSessionRepository(path).GetMatchesAsync(session.VerificationSessionId));
            Assert.Equal(DimensionMatchStatus.ExtraDimension, loaded.Status); Assert.Equal("Face:extra", loaded.StepGeometryReference); Assert.Equal(24, loaded.MeasuredValue); Assert.Equal("Should be shown", loaded.Review!.Comment); Assert.True(loaded.Review.RequiresDesignUpdate);
            using var audit = JsonDocument.Parse(details); Assert.Equal(1, audit.RootElement.GetProperty("extraStepCount").GetInt32());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-INF-EXTRA-002 preserves extra review state across repository replacement")]
    public async Task Extra_review_state_is_keyed_by_step_reference()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-extra-recompare-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid(); await CreateCurrentDocumentsAsync(path, step, dxf, stepVersion, dxfVersion);
            var repository = new SqliteVerificationSessionRepository(path); var session = await repository.GetOrCreateAsync(step, dxf) with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched };
            var first = new DimensionMatch { DimensionMatchId = Guid.NewGuid(), VerificationSessionId = session.VerificationSessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, DxfDimensionReference = -1, StepGeometryReference = "Edge:stable", Status = DimensionMatchStatus.ExtraDimension, Review = new DimensionReview { Comment = "Keep me", RequiresDesignUpdate = true } };
            await repository.ReplaceResultsAsync(session, new[] { first }, DocumentAuditAction.ToleranceComparisonApplied, "checker", "{}");
            var replacement = first with { DimensionMatchId = Guid.NewGuid(), Review = null, MeasuredValue = 7 };
            await repository.ReplaceResultsAsync(session, new[] { replacement }, DocumentAuditAction.ToleranceComparisonApplied, "checker", "{}");
            var loaded = Assert.Single(await repository.GetMatchesAsync(session.VerificationSessionId)); Assert.Equal("Keep me", loaded.Review!.Comment); Assert.True(loaded.Review.RequiresDesignUpdate); Assert.Equal(7, loaded.MeasuredValue);
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-INF-EXTRA-003 rejects stale versions without writing extra results")]
    public async Task Stale_version_validation_is_explicit()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-extra-stale-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new SqliteVerificationSessionRepository(path); var session = await repository.GetOrCreateAsync(Guid.NewGuid(), Guid.NewGuid());
            var extra = new DimensionMatch { VerificationSessionId = session.VerificationSessionId, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:stale", DxfDimensionReference = -1 };
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReplaceResultsAsync(session with { StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid() }, new[] { extra }, DocumentAuditAction.ToleranceComparisonApplied, "checker", "{}"));
            Assert.Empty(await repository.GetMatchesAsync(session.VerificationSessionId));
        }
        finally { await DeleteDatabaseAsync(path); }
    }
    [Fact(DisplayName = "TD-INF-001 replaces SQLite results and persists unmatched audit details atomically")]
    public async Task Session_pair_is_unique_and_results_are_replaced_atomically()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-session-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new SqliteVerificationSessionRepository(path);
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var session = await repository.GetOrCreateAsync(step, dxf);
            var again = await repository.GetOrCreateAsync(step, dxf);
            Assert.Equal(session.VerificationSessionId, again.VerificationSessionId);
            var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid();
            var matched = new DimensionMatch { VerificationSessionId = session.VerificationSessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, DxfDimensionReference = 1, StepGeometryReference = "Edge:1", Status = DimensionMatchStatus.Matched, Confidence = MatchConfidence.High, ConfidenceScore = 1 };
            await repository.ReplaceResultsAsync(session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched }, new[] { matched }, DocumentAuditAction.DimensionsMatched, "maker", JsonSerializer.Serialize(new { verificationSessionId = session.VerificationSessionId, matches = 1 }));
            Assert.Single(await repository.GetMatchesAsync(session.VerificationSessionId));
            var replacement = matched with { DxfDimensionReference = 2, Status = DimensionMatchStatus.Unmatched, StepGeometryReference = null, Confidence = null, ConfidenceScore = null, ReasonCode = "NoCorrespondingGeometryFound", RequiresManualReview = true };
            replacement = replacement with { DimensionType = DimensionType.Radial, NominalValue = 7.5, DimensionText = "R7.5", ReasonExplanation = "No corresponding geometry found in STEP model." };
            var details = JsonSerializer.Serialize(new { verificationSessionId = session.VerificationSessionId, unmatchedDimensions = 1, reasonBreakdown = new { NoCorrespondingGeometryFound = 1 } });
            await repository.ReplaceResultsAsync(session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched }, new[] { replacement }, DocumentAuditAction.UnmatchedDimensionsFlagged, "maker", details);
            var rows = await repository.GetMatchesAsync(session.VerificationSessionId);
            Assert.Single(rows); Assert.Equal(2, rows[0].DxfDimensionReference); Assert.Equal(DimensionMatchStatus.Unmatched, rows[0].Status);
            Assert.Equal(DimensionType.Radial, rows[0].DimensionType); Assert.Equal(7.5, rows[0].NominalValue); Assert.Equal("R7.5", rows[0].DimensionText); Assert.Equal("No corresponding geometry found in STEP model.", rows[0].ReasonExplanation);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT Action,UserId,TimestampUtc,DetailsJson FROM DocumentAuditLog ORDER BY TimestampUtc DESC LIMIT 1";
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal((int)DocumentAuditAction.UnmatchedDimensionsFlagged, reader.GetInt32(0)); Assert.Equal("maker", reader.GetString(1)); Assert.True(DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind).Kind == DateTimeKind.Utc);
            using var persistedDetails = JsonDocument.Parse(reader.GetString(3)); Assert.Equal(1, persistedDetails.RootElement.GetProperty("reasonBreakdown").GetProperty("NoCorrespondingGeometryFound").GetInt32());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-INF-001 round-trips tolerance evidence and audit counts")]
    public async Task Tolerance_metadata_and_audit_details_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-tolerance-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new SqliteVerificationSessionRepository(path);
            var session = await repository.GetOrCreateAsync(Guid.NewGuid(), Guid.NewGuid());
            var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid();
            var match = new DimensionMatch { VerificationSessionId = session.VerificationSessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, DxfDimensionReference = 1, StepGeometryReference = "Edge:1", GeometryType = "Line", Status = DimensionMatchStatus.Mismatched, ReasonCode = "OutsideTolerance", Confidence = MatchConfidence.High, ConfidenceScore = 1, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 10.2, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .1, ToleranceUnit = "mm", ToleranceSource = ToleranceSource.Default, RequiresManualReview = true };
            var details = JsonSerializer.Serialize(new { verificationSessionId = session.VerificationSessionId, toleranceSource = "Default", toleranceThresholdMm = .1, comparedDimensions = 1, matchedDimensions = 0, mismatchedDimensions = 1, recomparison = false });
            await repository.ReplaceResultsAsync(session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, ToleranceThresholdMm = .1 }, new[] { match }, DocumentAuditAction.ToleranceComparisonApplied, "checker", details);
            var persisted = Assert.Single(await repository.GetMatchesAsync(session.VerificationSessionId));
            Assert.Equal(10.2, persisted.MeasuredValue); Assert.Equal((-.1, .1), (persisted.EffectiveLowerTolerance, persisted.EffectiveUpperTolerance));
            Assert.Equal(ToleranceSource.Default, persisted.ToleranceSource); Assert.Equal(DimensionMatchStatus.Mismatched, persisted.Status);
            var loadedSession = await repository.GetAsync(session.VerificationSessionId); Assert.Equal(.1, loadedSession!.ToleranceThresholdMm);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT Action,UserId,DetailsJson FROM DocumentAuditLog";
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal((int)DocumentAuditAction.ToleranceComparisonApplied, reader.GetInt32(0)); Assert.Equal("checker", reader.GetString(1));
            using var audit = JsonDocument.Parse(reader.GetString(2)); Assert.Equal(1, audit.RootElement.GetProperty("comparedDimensions").GetInt32()); Assert.Equal(1, audit.RootElement.GetProperty("mismatchedDimensions").GetInt32());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    private static async Task DeleteDatabaseAsync(string path)
    {
        if (!File.Exists(path)) return;
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                SqliteConnection.ClearAllPools();
                await Task.Delay(25);
            }
        }
    }

    [Fact(DisplayName = "TD-INF-001 migrates legacy match metadata and retains unmatched audit rows")]
    public async Task Legacy_dimension_match_schema_is_migrated_and_audit_rows_are_retained()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-legacy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            { await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE DimensionMatch (DimensionMatchId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, DxfDimensionReference INTEGER NOT NULL, StepDocumentVersionId TEXT NOT NULL, StepGeometryReference TEXT NULL, GeometryType TEXT NULL, ConfidenceScore REAL NULL, Confidence INTEGER NULL, RequiresManualReview INTEGER NOT NULL, Status INTEGER NOT NULL, ReasonCode TEXT NULL)"; await command.ExecuteNonQueryAsync(); }
            _ = new SqliteVerificationSessionRepository(path);
            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await verify.OpenAsync();
            await using var columns = verify.CreateCommand(); columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DimensionMatch') WHERE name IN ('DimensionType','NominalValue','DimensionText','ReasonExplanation','MeasuredValue','EffectiveLowerTolerance','EffectiveUpperTolerance','ToleranceUnit','ToleranceSource')";
            Assert.Equal(9L, await columns.ExecuteScalarAsync());
            await using var insert = verify.CreateCommand(); insert.CommandText = "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ('audit',NULL,15,'maker','2026-01-01T00:00:00Z','{}')"; await insert.ExecuteNonQueryAsync();
            await using var count = verify.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog"; Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-05 and TD-09 persist unmatched review metadata, comments, status, and audit across reload")]
    public async Task Unmatched_review_round_trips_after_repository_reopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-review-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid(); var matchId = Guid.NewGuid();
            var repository = new SqliteVerificationSessionRepository(path); var session = await repository.GetOrCreateAsync(step, dxf); var sessionId = session.VerificationSessionId;
            var match = new DimensionMatch { DimensionMatchId = matchId, VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, DxfDimensionReference = 12, Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound", NominalValue = 25, DimensionText = "25", DxfLayer = "DIM", PreviewAnchor = "5,6", Review = new DimensionReview { DimensionMatchId = matchId, Comment = "Expected missing", Status = DimensionResolutionStatus.Open } };
            var details = JsonSerializer.Serialize(new { stepDocumentId = step, dxfDocumentId = dxf, missingDxfCount = 1, timestampUtc = DateTime.UtcNow, triggeredBy = "maker", verificationSessionId = sessionId, stepDocumentVersionId = stepVersion, dxfDocumentVersionId = dxfVersion, extraStepCount = 0 });
            await repository.ReplaceResultsAsync(session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched }, new[] { match }, DocumentAuditAction.UnmatchedDimensionsFlagged, "maker", details);
            await repository.UpdateReviewAsync(sessionId, matchId, match.Review! with { Comment = "Expected missing – reviewed", Status = DimensionResolutionStatus.Accepted });
            var reloaded = new SqliteVerificationSessionRepository(path); var persisted = Assert.Single(await reloaded.GetMatchesAsync(sessionId));
            Assert.Equal("DIM", persisted.DxfLayer); Assert.Equal("5,6", persisted.PreviewAnchor); Assert.Equal("Expected missing – reviewed", persisted.Review!.Comment); Assert.Equal(DimensionResolutionStatus.Accepted, persisted.Review.Status);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT DetailsJson FROM DocumentAuditLog WHERE Action=$action"; command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.UnmatchedDimensionsFlagged);
            using var audit = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!); Assert.Equal(step, audit.RootElement.GetProperty("stepDocumentId").GetGuid()); Assert.Equal(dxf, audit.RootElement.GetProperty("dxfDocumentId").GetGuid()); Assert.Equal(1, audit.RootElement.GetProperty("missingDxfCount").GetInt32()); Assert.Equal("maker", audit.RootElement.GetProperty("triggeredBy").GetString());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-REVIEW-01 preserves unmatched review state across result replacement")]
    public async Task Unmatched_review_survives_recomparison_for_same_dxf_entity()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-recompare-review-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new SqliteVerificationSessionRepository(path);
            var session = await repository.GetOrCreateAsync(Guid.NewGuid(), Guid.NewGuid());
            var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid(); var matchId = Guid.NewGuid();
            var persistedSession = session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched };
            var initial = new DimensionMatch
            {
                DimensionMatchId = matchId, VerificationSessionId = session.VerificationSessionId,
                StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion,
                DxfDimensionReference = 42, Status = DimensionMatchStatus.Unmatched,
                ReasonCode = "NoCorrespondingGeometryFound",
                Review = new DimensionReview { DimensionMatchId = matchId, Comment = "Review this missing dimension", Status = DimensionResolutionStatus.Rejected }
            };
            await repository.ReplaceResultsAsync(persistedSession, new[] { initial }, DocumentAuditAction.UnmatchedDimensionsFlagged, "maker", "{}");

            var replacement = initial with { DimensionMatchId = Guid.NewGuid(), Review = null, ReasonExplanation = "No corresponding geometry found in the re-comparison." };
            await repository.ReplaceResultsAsync(persistedSession, new[] { replacement }, DocumentAuditAction.ToleranceComparisonApplied, "maker", "{}");

            var result = Assert.Single(await repository.GetMatchesAsync(session.VerificationSessionId));
            Assert.Equal(42, result.DxfDimensionReference);
            Assert.Equal("Review this missing dimension", result.Review!.Comment);
            Assert.Equal(DimensionResolutionStatus.Rejected, result.Review.Status);
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-REVIEW-04 round-trips GD&T source metadata and review audit payloads")]
    public async Task Gdt_source_metadata_and_review_survive_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-gdt-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new SqliteVerificationSessionRepository(path);
            var session = await repository.GetOrCreateAsync(Guid.NewGuid(), Guid.NewGuid());
            var result = new GdtResult
            {
                VerificationSessionId = session.VerificationSessionId, Identifier = "DXF:gdt-1", ControlType = GdtControlType.Unknown,
                SourceDocument = "DXF:drawing", SourceDocumentVersion = "v1", SourceLocation = "entity:17", RawText = "◇ 0.2",
                EntityHandle = "17", Layer = "GD&T", BlockName = "BLOCK_A", ViewName = "VIEW_1", Diagnostics = new[] { "malformed modifier retained" },
                Status = VerificationStatus.NotVerified, Reason = GdtResult.UnsupportedReason
            };
            await repository.ReplaceGdtResultsAsync(session.VerificationSessionId, new[] { result }, "checker");
            var reloaded = Assert.Single(await repository.GetGdtResultsAsync(session.VerificationSessionId));
            Assert.Equal(result.EntityHandle, reloaded.EntityHandle); Assert.Equal(result.Layer, reloaded.Layer); Assert.Equal(result.BlockName, reloaded.BlockName); Assert.Equal(result.ViewName, reloaded.ViewName); Assert.Equal(result.Diagnostics, reloaded.Diagnostics);
            await repository.UpdateGdtReviewAsync(session.VerificationSessionId, reloaded.GdtResultId, "Review", true, "checker");
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog WHERE Action=$action"; command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.UnsupportedGdtReviewChanged); Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-003 round-trips structured missing-dimension location metadata")]
    public async Task Structured_location_metadata_survives_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-location-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid();
            await CreateCurrentDocumentsAsync(path, step, dxf, stepVersion, dxfVersion);
            var repository = new SqliteVerificationSessionRepository(path);
            var session = await repository.GetOrCreateAsync(step, dxf);
            session = session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched };
            var matchId = Guid.NewGuid();
            var match = new DimensionMatch { DimensionMatchId = matchId, VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dxfVersion, StepDocumentVersionId = stepVersion, DxfDimensionReference = 9, Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound", NominalValue = 50, DimensionText = "50.00", DxfLayer = "DIM", BlockName = "B1", ViewName = "Front", LocationX = 11.25, LocationY = 22.5 };
            await repository.ReplaceResultsAsync(session, new[] { match }, DocumentAuditAction.UnmatchedDimensionsFlagged, "checker", "{}");
            var loaded = Assert.Single(await new SqliteVerificationSessionRepository(path).GetMatchesAsync(session.VerificationSessionId));
            Assert.Equal("B1", loaded.BlockName); Assert.Equal("Front", loaded.ViewName); Assert.Equal(11.25, loaded.LocationX); Assert.Equal(22.5, loaded.LocationY);
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    [Fact(DisplayName = "TD-005 writes review audit payload with actor, identifier, action, and UTC timestamp")]
    public async Task Review_update_writes_structured_audit_entry()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-review-audit-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var step = Guid.NewGuid(); var dxf = Guid.NewGuid(); var stepVersion = Guid.NewGuid(); var dxfVersion = Guid.NewGuid();
            await CreateCurrentDocumentsAsync(path, step, dxf, stepVersion, dxfVersion);
            var repository = new SqliteVerificationSessionRepository(path); var session = await repository.GetOrCreateAsync(step, dxf);
            session = session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched }; var matchId = Guid.NewGuid();
            var match = new DimensionMatch { DimensionMatchId = matchId, VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dxfVersion, StepDocumentVersionId = stepVersion, DxfDimensionReference = 3, Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound" };
            await repository.ReplaceResultsAsync(session, new[] { match }, DocumentAuditAction.UnmatchedDimensionsFlagged, "checker", "{}");
            await repository.UpdateReviewAsync(session.VerificationSessionId, matchId, new DimensionReview { DimensionMatchId = matchId, Comment = "Not modeled in 3D", Status = DimensionResolutionStatus.Open }, "checker", DocumentAuditAction.CommentAdded);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT UserId,Action,TimestampUtc,DetailsJson FROM DocumentAuditLog WHERE Action=$action"; command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.CommentAdded);
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal("checker", reader.GetString(0)); Assert.Equal((int)DocumentAuditAction.CommentAdded, reader.GetInt32(1)); Assert.Equal(DateTimeKind.Utc, DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind).Kind);
            using var details = JsonDocument.Parse(reader.GetString(3)); Assert.Equal(matchId, details.RootElement.GetProperty("dimensionMatchId").GetGuid()); Assert.Equal("checker", details.RootElement.GetProperty("username").GetString());
        }
        finally { await DeleteDatabaseAsync(path); }
    }

    private static async Task CreateCurrentDocumentsAsync(string path, Guid stepId, Guid dxfId, Guid stepVersionId, Guid dxfVersionId)
    {
        var documents = new SqliteDocumentRepository(path);
        var stepVersion = new DocumentVersion { VersionId = stepVersionId, DocumentId = stepId, VersionNumber = 1, StoredRelativePath = "fixture.step", FileSizeBytes = 1 };
        var dxfVersion = new DocumentVersion { VersionId = dxfVersionId, DocumentId = dxfId, VersionNumber = 1, StoredRelativePath = "fixture.dxf", FileSizeBytes = 1 };
        await documents.CreateWithVersionAsync(new Document { DocumentId = stepId, OriginalFileName = "fixture.step", FileSizeBytes = 1, DocumentType = DrawVerify.Core.Enums.DocumentType.StepModel, GeometryStatus = DrawVerify.Core.Enums.GeometryStatus.Processed, CurrentVersion = 1, Version = stepVersion, Versions = new[] { stepVersion } }, stepVersion);
        await documents.CreateWithVersionAsync(new Document { DocumentId = dxfId, OriginalFileName = "fixture.dxf", FileSizeBytes = 1, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, VerificationStatus = VerificationStatus.DimensionsExtracted, CurrentVersion = 1, Version = dxfVersion, Versions = new[] { dxfVersion } }, dxfVersion);
    }
}
