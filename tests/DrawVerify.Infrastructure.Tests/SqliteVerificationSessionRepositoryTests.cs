using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteVerificationSessionRepositoryTests
{
    [Fact]
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

    [Fact]
    public async Task Legacy_dimension_match_schema_is_migrated_and_audit_rows_are_retained()
    {
        var path = Path.Combine(Path.GetTempPath(), "drawverify-legacy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            { await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE DimensionMatch (DimensionMatchId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, DxfDimensionReference INTEGER NOT NULL, StepDocumentVersionId TEXT NOT NULL, StepGeometryReference TEXT NULL, GeometryType TEXT NULL, ConfidenceScore REAL NULL, Confidence INTEGER NULL, RequiresManualReview INTEGER NOT NULL, Status INTEGER NOT NULL, ReasonCode TEXT NULL)"; await command.ExecuteNonQueryAsync(); }
            _ = new SqliteVerificationSessionRepository(path);
            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await verify.OpenAsync();
            await using var columns = verify.CreateCommand(); columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DimensionMatch') WHERE name IN ('DimensionType','NominalValue','DimensionText','ReasonExplanation')";
            Assert.Equal(4L, await columns.ExecuteScalarAsync());
            await using var insert = verify.CreateCommand(); insert.CommandText = "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ('audit',NULL,15,'maker','2026-01-01T00:00:00Z','{}')"; await insert.ExecuteNonQueryAsync();
            await using var count = verify.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog"; Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally { await DeleteDatabaseAsync(path); }
    }
}
