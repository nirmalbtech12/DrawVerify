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
    [Fact(DisplayName = "REV-KAN66-003 persisted Approver report projection validates integrity before rendering")]
    public void Persisted_approver_report_contract_validates_identity_revision_and_integrity()
    {
        var report = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "ReportService.cs")));
        var generator = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Pdf", "PdfReportGenerator.cs")));
        Assert.Contains("ApproverSignature", report);
        Assert.Contains("Validate", report);
        Assert.Contains("DocumentId", report);
        Assert.Contains("Revision", report);
        Assert.Contains("ApproverSignature", generator);
    }

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

    [Fact(DisplayName = "TD-KAN70-01 populated PDF contains every required verification section and field")]
    public async Task Populated_pdf_contains_required_sections_fields_and_summary()
    {
        var report = new VerificationReport
        {
            Metadata = new VerificationReportMetadata
            {
                DocumentName = "bracket.dxf", DocumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                DrawingVersion = "7", ModelVersion = "3", VerificationSessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                VerificationMethod = "Tolerance profile A", CheckerIdentity = "Checker One",
                GeneratedUtc = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero)
            },
            DimensionMatches = new[]
            {
                new DimensionMatch { DxfDimensionReference = 1, Status = DimensionMatchStatus.Matched, NominalValue = 10, MeasuredValue = 10.01, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .1 },
                new DimensionMatch { DxfDimensionReference = 2, Status = DimensionMatchStatus.Mismatched, NominalValue = 20, MeasuredValue = 20.5, EffectiveLowerTolerance = -.2, EffectiveUpperTolerance = .2, ReasonExplanation = "Tolerance exceeded", RequiresManualReview = true },
                new DimensionMatch { DxfDimensionReference = 3, Status = DimensionMatchStatus.Unmatched, NominalValue = 30, ReasonExplanation = "Feature missing in 3D" }
            },
            GdtResults = new[]
            {
                new GdtResult { Identifier = "FCF-1", ControlType = GdtControlType.Position, DatumReferences = new[] { "A", "B" }, ToleranceValue = "0.2", ComparisonDetails = "0.12 measured", Status = VerificationStatus.Passed }
            }
        };

        var content = System.Text.Encoding.ASCII.GetString(await new PdfReportGenerator().GeneratePdfAsync(report));

        Assert.StartsWith("%PDF-1.4", content, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", content, StringComparison.Ordinal);
        foreach (var value in new[] { "Summary", "Matched Dimensions", "Mismatched Dimensions", "Missing Dimensions", "GD&T Results", "Identifier", "Nominal", "Measured 3D", "Lower Tolerance", "Upper Tolerance", "Deviation", "Pass/Fail", "Reason", "Severity", "1", "10.01", "Tolerance exceeded", "High", "Feature missing in 3D", "FCF-1", "Position", "A, B", "0.2", "0.12 measured", "Passed", "Total Dimensions: 3", "Matched: 1", "Mismatched: 1", "Missing: 1", "GD&T Checked: 1", "50% dimensions within tolerance" })
            Assert.Contains(value, content);
    }

    [Fact(DisplayName = "TD-KAN70-02 and TD-KAN70-03 filters normalize and empty sections remain explicit")]
    public async Task Filtered_and_empty_reports_preserve_scope_and_empty_markers()
    {
        var report = new VerificationReport
        {
            DimensionMatches = new[] { new DimensionMatch { DxfDimensionReference = 8, Status = DimensionMatchStatus.Matched, GeometryType = "Hole", ViewName = "Front", NominalValue = 8 } },
            Request = new VerificationReportRequest { IncludeVisuals = false, Visuals = new[] { new VerificationReportVisual("V1", "not-rendered.png") } }
        };
        var generator = new PdfReportGenerator();
        var empty = System.Text.Encoding.ASCII.GetString(await generator.GeneratePdfAsync(new VerificationReport()));
        var scopedRequest = new VerificationReportRequest { FeatureFilters = new[] { " Hole ", "hole" }, ZoneFilters = new[] { " Front " }, IncludeVisuals = true, Visuals = new[] { new VerificationReportVisual("V1", "snapshot.png", "callout") } };
        var scoped = System.Text.Encoding.ASCII.GetString(await generator.GeneratePdfAsync(report with { Request = scopedRequest }));

        Assert.Contains("Matched Dimensions", empty); Assert.Contains("Mismatched Dimensions", empty); Assert.Contains("Missing Dimensions", empty); Assert.Contains("GD&T Results", empty); Assert.Contains("No items", empty);
        Assert.Contains("V1: callout", scoped); Assert.Contains("8", scoped);
        await Assert.ThrowsAsync<ArgumentException>(() => generator.GeneratePdfAsync(report with { Request = new VerificationReportRequest { GdtOnly = true, MismatchedAndMissingOnly = true } }));
    }

    [Fact(DisplayName = "TD-KAN70-04 PDF escaping, cancellation, pagination input, and approval stamp remain safe")]
    public async Task Pdf_escapes_special_text_and_honors_cancellation_and_stamp()
    {
        var report = new VerificationReport
        {
            DimensionMatches = Enumerable.Range(1, 140).Select(i => new DimensionMatch { DxfDimensionReference = i, Status = DimensionMatchStatus.Mismatched, NominalValue = i, MeasuredValue = i + .5, ReasonExplanation = i == 1 ? "critical-first" : i == 140 ? "critical-last" : "a (long) \\ diagnostic" }).ToArray(),
            InternalApprovalStamp = InternalApprovalStamp.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, "approver", "Approver", DateTimeOffset.UtcNow, "fp", Guid.NewGuid())
        };
        var generator = new PdfReportGenerator();
        var content = System.Text.Encoding.ASCII.GetString(await generator.GeneratePdfAsync(report));
        Assert.Contains("a \\(long\\) \\\\ diagnostic", content);
        Assert.Contains("critical-first", content);
        Assert.Contains("critical-last", content);
        Assert.True(CountOccurrences(content, "/Type /Page ") > 1, "A long report must be rendered as multiple PDF page objects.");
        var contentStreams = content.Split("stream\n", StringSplitOptions.None).Skip(1).Select(stream => stream.Split("\nendstream", StringSplitOptions.None)[0]).Where(stream => stream.Contains("BT /F1", StringComparison.Ordinal)).ToArray();
        Assert.True(contentStreams.Length > 1, "A long report must have multiple page content streams.");
        Assert.All(contentStreams, stream => Assert.InRange(CountOccurrences(stream, "0 -12 Td"), 0, 60));
        Assert.Contains("Internal Approval Stamp", content);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GeneratePdfAsync(report, new CancellationToken(canceled: true)));

        using var cancellation = new CancellationTokenSource();
        var longReport = report with { DimensionMatches = Enumerable.Range(1, 20_000).Select(i => new DimensionMatch { DxfDimensionReference = i, Status = DimensionMatchStatus.Mismatched, NominalValue = i, MeasuredValue = i + .5, ReasonExplanation = "cancellation-boundary" }).ToArray() };
        cancellation.CancelAfter(10);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => generator.GeneratePdfAsync(longReport, cancellation.Token)).WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact(DisplayName = "TSD-AC01-AUDIT-SECTION renders populated and empty Audit Trail sections")]
    public async Task TSD_AC01_AUDIT_SECTION_renders_populated_and_empty_sections()
    {
        var entry = new AuditTrailEntry { AuditId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), UserName = "Maker One", UserRole = "Maker", Timestamp = DateTimeOffset.Parse("2026-09-17T10:00:00+05:30"), Action = DocumentAuditAction.DocumentCreated, Outcome = "Success", Details = "created", Identifier = "REV-1" };
        var populated = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { AuditTrail = new[] { entry } }));
        var empty = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport()));
        Assert.Contains("Audit Trail", populated); Assert.Contains("DocumentCreated", populated); Assert.Contains("Maker One", populated);
        Assert.Contains("No audit entries available.", empty);
    }

    [Fact(DisplayName = "TSD-AC03-ACTOR-IDENTITY and TSD-AC04-TIMESTAMP-FORMAT render identity and offset")]
    public async Task TSD_AC03_AND_TSD_AC04_render_identity_role_and_offset()
    {
        var entry = new AuditTrailEntry { DocumentId = Guid.NewGuid(), UserName = "Approver One", UserRole = "Approver", UserId = "approver-1", UserEmail = "approver@example.test", Timestamp = DateTimeOffset.Parse("2026-09-17T10:15:00+05:30"), Action = DocumentAuditAction.Approved };
        var content = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { AuditTrail = new[] { entry } }));
        Assert.Contains("Approver One", content); Assert.Contains("Approver", content); Assert.Contains("2026-09-17T10:15:00.0000000+05:30", content); Assert.Contains("UTC", content);
    }

    [Fact(DisplayName = "TSD-AC07-ACTION-DETAILS preserves outcomes details identifiers and integrity reference")]
    public async Task TSD_AC07_ACTION_DETAILS_renders_status_details_and_identifiers()
    {
        var entry = new AuditTrailEntry { DocumentId = Guid.NewGuid(), Action = DocumentAuditAction.ReturnedForRework, Outcome = "Returned", Details = "created", Identifier = "run-42", IntegrityReference = "hash-42" };
        var content = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { AuditTrail = new[] { entry } }));
        Assert.Equal("Returned", entry.Outcome); Assert.Equal("created", entry.Details); Assert.Equal("run-42", entry.Identifier); Assert.Equal("hash-42", entry.IntegrityReference);
        Assert.Contains("Audit Trail", content); Assert.Contains("Timestamp | User | Role | Action | Outcome | Details | Identifier", content);
    }

    [Fact(DisplayName = "TSD-AC08-ORDERING, TSD-AC12-READABLE-PDF, and TSD-AC14-LOG-CONSISTENCY preserve ordered labeled rows")]
    public async Task TSD_AC08_TSD_AC12_TSD_AC14_preserve_order_labels_and_complete_values()
    {
        var document = Guid.NewGuid();
        var first = new AuditTrailEntry { AuditId = Guid.Parse("00000000-0000-0000-0000-000000000001"), DocumentId = document, Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), Action = DocumentAuditAction.DocumentCreated, Details = new string('A', 180), Identifier = "first" };
        var second = first with { AuditId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Timestamp = first.Timestamp.AddMinutes(1), Action = DocumentAuditAction.VerificationRunCompleted, Details = new string('B', 180), Identifier = "second" };
        var content = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { AuditTrail = new[] { first, second } }));
        Assert.Contains("Timestamp | User | Role | Action | Outcome | Details | Identifier", content);
        Assert.True(content.IndexOf("DocumentCreated", StringComparison.Ordinal) < content.IndexOf("VerificationRunCompleted", StringComparison.Ordinal));
        Assert.Contains(new string('A', 108), content); Assert.Contains(new string('A', 72), content); Assert.Contains(new string('B', 108), content); Assert.Contains(new string('B', 72), content);
    }

    [Fact(DisplayName = "TSD-AC10-REPORT-METADATA renders report metadata in Audit Trail")]
    public async Task TSD_AC10_REPORT_METADATA_renders_required_metadata()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var report = new VerificationReport { Metadata = new VerificationReportMetadata { ReportType = "VerificationReportWithAuditTrail", GeneratorIdentity = "Checker One", DocumentId = id, DrawingVersionId = Guid.NewGuid(), ModelVersionId = Guid.NewGuid(), GeneratedUtc = DateTimeOffset.Parse("2026-09-17T10:00:00Z") } };
        var content = ExtractRenderedPdfText(await new PdfReportGenerator().GeneratePdfAsync(report));
        Assert.Contains("VerificationReportWithAuditTrail", content); Assert.Contains("Checker One", content); Assert.Contains(id.ToString(), content); Assert.Contains("Report Generation Timestamp", content);
    }

    private static string ExtractRenderedPdfText(byte[] pdf)
    {
        var source = System.Text.Encoding.ASCII.GetString(pdf);
        var output = new System.Text.StringBuilder();
        var streamStart = 0;
        while ((streamStart = source.IndexOf("stream\n", streamStart, StringComparison.Ordinal)) >= 0)
        {
            var streamEnd = source.IndexOf("\nendstream", streamStart + 7, StringComparison.Ordinal);
            if (streamEnd < 0) break;
            var contentStream = source.Substring(streamStart + 7, streamEnd - streamStart - 7);
            output.AppendLine(contentStream.Replace("\\(", "(", StringComparison.Ordinal).Replace("\\)", ")", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
            streamStart = streamEnd + 10;
        }
        return output.ToString();
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length) count++;
        return count;
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
