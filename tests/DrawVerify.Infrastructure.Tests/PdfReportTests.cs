namespace DrawVerify.Infrastructure.Tests;

using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for PDF report rendering and stamp placement.
/// </summary>
public class PdfReportTests
{
    [Fact(DisplayName = "TD-APP-004 report rendering reflects replacement delta values")]
    public void Report_uses_current_recomparison_delta_and_not_the_stale_value()
    {
        var markdown = new VerificationReport { DimensionMatches = new[] { new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 1, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 10.275 } } }.ToMarkdown();
        Assert.Contains("Signed delta (DXF − STEP): -0.275 mm", markdown); Assert.DoesNotContain("+0.150", markdown);
    }
    [Fact(DisplayName = "TD-INFRA-001 persists report-export audit fields without delta schema columns")]
    public async Task Report_export_audit_round_trips_and_schema_remains_compatible()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-report-audit-{Guid.NewGuid():N}.db"); var sessionId = Guid.NewGuid();
        try
        {
            var repository = new SqliteAuditRepository(path);
            var entry = new AuditEntry { DocumentId = sessionId, UserId = "checker-id", Action = DocumentAuditAction.VerificationReportGenerated, TimestampUtc = DateTime.UtcNow, DetailsJson = JsonSerializer.Serialize(new { verificationSessionId = sessionId, mismatchedDimensions = 2, reportFormat = "Markdown" }) };
            await repository.WriteAsync(entry);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT DocumentId, UserId, Action, DetailsJson FROM DocumentAuditLog WHERE AuditId = $id"; command.Parameters.AddWithValue("$id", entry.AuditId.ToString());
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal(sessionId.ToString(), reader.GetString(0)); Assert.Equal("checker-id", reader.GetString(1)); Assert.Equal((int)DocumentAuditAction.VerificationReportGenerated, reader.GetInt32(2));
            using var details = JsonDocument.Parse(reader.GetString(3)); Assert.Equal(2, details.RootElement.GetProperty("mismatchedDimensions").GetInt32()); Assert.Equal("Markdown", details.RootElement.GetProperty("reportFormat").GetString());
            await reader.DisposeAsync(); command.CommandText = "SELECT name FROM pragma_table_info('DocumentAuditLog') WHERE name LIKE '%Delta%'"; Assert.Null(await command.ExecuteScalarAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
    [Fact(DisplayName = "TD-INFRA-002 emits the labeled unmatched Markdown section and required fields")]
    public void Markdown_report_contains_labeled_unmatched_section_and_required_fields()
    {
        var report = new VerificationReport { DimensionMatches = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 4, DimensionType = DimensionType.Linear, NominalValue = 25, ReasonCode = "NoCorrespondingGeometryFound", ReasonExplanation = "No corresponding geometry found in STEP model." },
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 5 }
        }};
        var markdown = report.ToMarkdown();
        Assert.Contains("## Unmatched Dimensions – Requires Manual Review", markdown);
        Assert.Contains("Reference: `4`", markdown);
        Assert.Contains("Value: 25", markdown);
        Assert.Contains("Tolerance:", markdown); Assert.Contains("Layer:", markdown); Assert.Contains("Text:", markdown);
        Assert.Contains("NoCorrespondingGeometryFound", markdown);
        Assert.DoesNotContain("Reference: `5`", markdown);
    }

    [Fact(DisplayName = "TD-INFRA-002 emits an explicit unmatched section for an empty report")]
    public void Markdown_report_has_an_explicit_unmatched_section_when_there_are_no_matches()
        => Assert.Contains("Unmatched Dimensions – Requires Manual Review", new VerificationReport().ToMarkdown());
}
