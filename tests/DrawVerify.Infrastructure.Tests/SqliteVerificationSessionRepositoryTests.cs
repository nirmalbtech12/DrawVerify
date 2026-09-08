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
            await repository.ReplaceResultsAsync(session with { StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = VerificationSessionStatus.DimensionsMatched }, new[] { replacement }, DocumentAuditAction.DimensionsMatchedPartial, "maker", "{\"rematched\":true}");
            var rows = await repository.GetMatchesAsync(session.VerificationSessionId);
            Assert.Single(rows); Assert.Equal(2, rows[0].DxfDimensionReference); Assert.Equal(DimensionMatchStatus.Unmatched, rows[0].Status);
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
}
