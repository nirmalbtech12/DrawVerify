namespace DrawVerify.Infrastructure.Tests;

using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Pdf;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for PDF report rendering and stamp placement.
/// </summary>
public class PdfReportTests
{
    [Fact(DisplayName = "TD-REPORT-01 PDF projection includes persisted Approver representation and metadata")]
    public void Pdf_report_contract_projects_approver_attestation_fields()
    {
        var generator = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Pdf", "PdfReportGenerator.cs")));
        Assert.Contains("ApproverSignature", generator);
        Assert.Contains("Approver", generator);
        Assert.Contains("VerificationReport.ApproverSignature", generator);
        Assert.Equal(nameof(VerificationReport.ApproverSignature), PdfReportGenerator.ApproverSignatureProjection);
    }

    [Fact(DisplayName = "TD-INFRA-002 renders every GD&T status and dedicated Cannot Validate review details")]
    public void Unsupported_gdt_section_contains_details_and_comment()
    {
        var result = new GdtResult
        {
            GdtResultId = Guid.NewGuid(), Identifier = "gdt-9", ControlType = GdtControlType.Unknown,
            Symbol = "◇", ToleranceValue = "0.2", Modifiers = "MMC", DatumReferences = new[] { "A" },
            SourceDocument = "drawing.dxf", SourceLocation = "entity:9", Status = VerificationStatus.CannotValidate,
            Reason = GdtResult.UnsupportedReason, ReviewComment = "Review this control"
        };
        var report = new VerificationReport { GdtResults = new[] { result } };
        var markdown = report.ToMarkdown();

        Assert.Contains("## GD&T Validation", markdown);
        Assert.Contains("gdt-9", markdown);
        Assert.Contains("0.2", markdown);
        Assert.Contains("MMC", markdown);
        Assert.Contains("Review this control", markdown);
        Assert.Equal(1, report.ComparisonSummary.CannotValidateGdtControls);
    }

    [Fact(DisplayName = "TD-INFRA-002 Markdown report includes all statuses and empty Cannot Validate section")]
    public void Markdown_report_contains_all_gdt_statuses_and_fields()
    {
        var results = Enum.GetValues<VerificationStatus>()
            .Where(status => status is VerificationStatus.Passed or VerificationStatus.Failed or VerificationStatus.Mismatched or VerificationStatus.NotVerified or VerificationStatus.CannotValidate)
            .Select((status, index) => new GdtResult
            {
                Identifier = $"gdt-{index}", ControlType = (GdtControlType)(index % 4 + 1), SourceDocument = "pair.step",
                SourceLocation = $"location:{index}", ControlledFeature = $"feature-{index}", ToleranceValue = "0.1",
                NormalizedModifiers = "MMC", DatumReferences = new[] { "A", "B" }, RuleVersion = "1.0.0",
                ComparisonDetails = "comparison evidence", ReviewComment = "checker note", RequiresEngineeringReview = true,
                Status = status, Reason = status is VerificationStatus.NotVerified or VerificationStatus.CannotValidate ? "ambiguous match" : null,
                ReviewDisposition = status == VerificationStatus.CannotValidate ? "Future scope" : null
            }).ToArray();
        var markdown = new VerificationReport { GdtResults = results }.ToMarkdown();

        Assert.Contains("## GD&T Validation", markdown);
        foreach (var status in new[] { "Passed", "Failed", "Mismatched", "NotVerified", "CannotValidate" }) Assert.Contains(status, markdown);
        Assert.Contains("## Cannot Validate / Manual Review Required", markdown);
        Assert.Contains("Future scope", markdown);
        Assert.Contains("1.0.0", markdown); Assert.Contains("comparison evidence", markdown); Assert.Contains("checker note", markdown);
        Assert.Contains("Engineering review: True", markdown);
        Assert.Contains("## GD&T Validation", new VerificationReport().ToMarkdown());
        Assert.Contains("None identified.", new VerificationReport().ToMarkdown());
    }

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

    [Fact(DisplayName = "TD-INFRA-002 PDF generator consumes normalized Cannot Validate report fields")]
    public async Task Pdf_report_contains_summary_and_not_verified_fields()
    {
        var report = new VerificationReport
        {
            GdtResults = new[]
            {
                new GdtResult
                {
                    Identifier = "gdt-pdf-1", ControlType = GdtControlType.Unknown, Symbol = "◇", ToleranceValue = "0.2",
                    SourceDocument = "drawing.dxf", SourceLocation = "entity:4", Status = VerificationStatus.CannotValidate,
                    Reason = GdtResult.UnsupportedReason, ReviewComment = "Needs checker review"
                }
            }
        };

        var pdf = await new PdfReportGenerator().GeneratePdfAsync(report);
        var content = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", content, StringComparison.Ordinal);
        Assert.Contains("GD&T Validation", content);
        Assert.Contains("gdt-pdf-1", content);
        Assert.Contains("Needs checker review", content);
        Assert.Contains("Cannot Validate / Manual Review Required", content);
        Assert.Contains("%%EOF", content);
    }

    [Fact(DisplayName = "TD-10 PDF generator preserves empty and legacy dimension reports")]
    public async Task Pdf_report_handles_empty_and_legacy_reports()
    {
        var generator = new PdfReportGenerator();
        var empty = System.Text.Encoding.ASCII.GetString(await generator.GeneratePdfAsync(new VerificationReport()));
        var legacy = System.Text.Encoding.ASCII.GetString(await generator.GeneratePdfAsync(new VerificationReport
        {
            DimensionMatches = new[] { new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 3, DimensionType = DimensionType.Linear, NominalValue = 12 } }
        }));

        Assert.Contains("None identified.", empty);
        Assert.Contains("Dimension Status Details", legacy);
        Assert.Contains("3", legacy);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
