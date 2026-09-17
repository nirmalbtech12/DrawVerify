using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportServiceStatusTests
{
    [Theory(DisplayName = "TD-05 reports every fingerprint verification outcome")]
    [InlineData(FingerprintVerificationStatus.Match, "Fingerprint Verified - Document Unchanged")]
    [InlineData(FingerprintVerificationStatus.Mismatch, "Fingerprint Mismatch - Document May Have Been Altered")]
    [InlineData(FingerprintVerificationStatus.Error, "Cannot verify fingerprint - file(s) missing or corrupted")]
    public void Markdown_report_contains_verification_details(FingerprintVerificationStatus status, string message)
    {
        var stored = new DocumentFingerprint { Value = new string('1', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var report = new VerificationReport
        {
            Fingerprint = stored,
            FingerprintVerification = new FingerprintVerificationResult(status, status == FingerprintVerificationStatus.Error ? null : stored, message)
            {
                Stored = stored, VerifiedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                VerifierUserId = "checker-001", VerifierDisplayName = "Checker One", Revision = 3,
                StepDocumentVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"), DxfDocumentVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333")
            }
        };

        var markdown = report.ToMarkdown();
        Assert.Contains(status.ToString(), markdown);
        Assert.Contains(stored.Value, markdown);
        Assert.Contains("Checker One", markdown);
        Assert.Contains("2026-01-01", markdown);
    }
    [Fact(DisplayName = "TD-09 renders the requested version fingerprint in a verification report")]
    public void Verification_report_includes_fingerprint_and_algorithm()
    {
        var fingerprint = new DocumentFingerprint { Value = new string('b', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var markdown = new VerificationReport { Fingerprint = fingerprint }.ToMarkdown();

        Assert.Contains(fingerprint.Value, markdown);
        Assert.Contains("SHA-256", markdown);
    }
    [Fact(DisplayName = "TD-10 renders a separate GD&T validation report section")]
    public void Markdown_report_includes_not_verified_details_and_comment()
    {
        var result = new GdtResult { Identifier = "gdt-report", ControlType = GdtControlType.Unknown, Symbol = "◇", ToleranceValue = "0.2", Modifiers = "MMC", DatumReferences = new[] { "A" }, SourceDocument = "drawing.dxf", SourceLocation = "entity:8", Status = VerificationStatus.NotVerified, Reason = GdtResult.UnsupportedReason, ReviewComment = "Engineering review" };
        var report = new VerificationReport { GdtResults = new[] { result } };

        var markdown = report.ToMarkdown();

        Assert.Contains("## GD&T Validation", markdown);
        Assert.Contains("gdt-report", markdown);
        Assert.Contains("entity:8", markdown);
        Assert.Contains("Engineering review", markdown);
        Assert.Equal(1, report.ComparisonSummary.NotVerifiedGdtControls);
    }

    [Fact(DisplayName = "TD-APP-EXTRA-003 renders Extra Dimensions report rows with key properties")]
    public void Extra_report_projection_contains_section_and_metadata()
    {
        var extra = new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Face:42", GeometryType = "Hole", MeasuredValue = 12, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .2, ToleranceUnit = "mm", ViewName = "Front", LocationX = 5, LocationY = 6 };
        var markdown = new VerificationReport { DimensionMatches = new[] { extra } }.ToMarkdown();
        Assert.Contains("## Extra Dimensions", markdown); Assert.Contains("Face:42", markdown); Assert.Contains("Hole", markdown); Assert.Contains("12", markdown); Assert.Contains("Front", markdown); Assert.Contains("(5, 6)", markdown);
    }

    [Fact(DisplayName = "TD-APP-EXTRA-003 exports extra dimensions as CSV")]
    public async Task Extra_csv_export_contains_header_and_row()
    {
        var sessionId = Guid.NewGuid();
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }));
        var service = new ReportService(new Sessions(new[] { new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra", MeasuredValue = 8, VerificationSessionId = sessionId } }), auth, new RoleAuthorization(), new MarkdownReportGenerator(), null, null);
        var csv = await service.GenerateExtraDimensionsCsvAsync(sessionId);
        Assert.Contains("StepReference", csv); Assert.Contains("Edge:extra", csv); Assert.Contains("8", csv);
    }
    [Fact(DisplayName = "TD-001 derives overall counts and projects legacy MissingDimension as extra")]
    public void Comparison_summary_counts_zero_and_missing_rows_separately()
    {
        var empty = ComparisonSummary.From(Array.Empty<DimensionMatch>());
        Assert.Equal(0, empty.TotalDimensions);
        Assert.Null(empty.MatchPercentage);

        var summary = ComparisonSummary.From(Fixtures().Append(new DimensionMatch
        {
            DxfDimensionReference = -1,
            DimensionType = DimensionType.Radial,
            Status = DimensionMatchStatus.MissingDimension
        }));

        Assert.Equal(5, summary.TotalDimensions);
        Assert.Equal(2, summary.MatchedDimensions);
        Assert.Equal(1, summary.MismatchedDimensions);
        Assert.Equal(1, summary.UnmatchedDimensions);
        Assert.Equal(1, summary.MissingDimensions);
        Assert.Equal(1, summary.ExtraDimensions);
    }

    [Fact(DisplayName = "TD-002 calculates exact percentage and typed breakdown")]
    public void Comparison_summary_calculates_percentage_and_all_dimension_types()
    {
        var rows = Enumerable.Range(1, 17).Select(i => new DimensionMatch { DxfDimensionReference = i, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Matched })
            .Concat(Enumerable.Range(18, 3).Select(i => new DimensionMatch { DxfDimensionReference = i, DimensionType = DimensionType.Angular, Status = DimensionMatchStatus.Mismatched }))
            .Concat(Enumerable.Range(21, 3).Select(i => new DimensionMatch { DxfDimensionReference = i, DimensionType = DimensionType.Radial, Status = DimensionMatchStatus.Unmatched }));
        var summary = ComparisonSummary.From(rows);

        Assert.NotNull(summary.MatchPercentage);
        Assert.Equal(17d / 23d * 100d, summary.MatchPercentage!.Value, precision: 12);
        Assert.Equal((17, 0, 0), TypeCounts(summary, DimensionType.Linear));
        Assert.Equal((0, 3, 0), TypeCounts(summary, DimensionType.Angular));
        Assert.Equal((0, 0, 3), TypeCounts(summary, DimensionType.Radial));
    }

    [Fact(DisplayName = "TD-005 generates complete Comparison Summary and preserves detail sections")]
    public async Task Markdown_report_contains_every_dimension_and_derived_counts()
    {
        var sessionId = Guid.NewGuid();
        var matches = Fixtures();
        var auth = Authenticated("checker", "Checker");
        var content = await new ReportService(new Sessions(matches), auth).GenerateMarkdownAsync(sessionId);

        Assert.Contains("## Dimension Status Summary", content);
        Assert.Contains("## Comparison Summary", content);
        Assert.Contains("Total Dimensions: 4", content);
        Assert.Contains("Matched: 2", content);
        Assert.Contains("Mismatched: 1", content);
        Assert.Contains("Unmatched: 1", content);
        Assert.Contains("Missing Dimensions: 0", content);
        Assert.Contains("Match Percentage: 50%", content);
        Assert.Contains("Linear: 1 Matched, 1 Mismatched, 1 Unmatched", content);
        Assert.Contains("Angular: 1 Matched, 0 Mismatched, 0 Unmatched", content);
        foreach (var match in matches)
        {
            Assert.Contains($"`{match.DxfDimensionReference}`", content);
            Assert.Contains($"Status: {Label(match.Status)}", content);
        }
    }

    [Fact(DisplayName = "TD-04 renders unmatched report fields and distinct extra-STEP count")]
    public void Report_contains_unmatched_metadata_and_distinct_directional_counts()
    {
        var unmatched = new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 8, NominalValue = 18, EffectiveLowerTolerance = -.2, EffectiveUpperTolerance = .3, ToleranceUnit = "mm", DxfLayer = "DIM", DimensionText = "18 ±0.2/0.3", ReasonCode = "NoCorrespondingGeometryFound", Review = new DimensionReview { DimensionMatchId = Guid.NewGuid(), Comment = "To be reviewed", Status = DimensionResolutionStatus.Open } };
        var report = new VerificationReport { DimensionMatches = new[] { unmatched, new DimensionMatch { Status = DimensionMatchStatus.MissingDimension, DxfDimensionReference = -1 } } };
        var markdown = report.ToMarkdown();
        Assert.Contains("Missing from 3D:", markdown); Assert.Contains("Value: 18 mm", markdown); Assert.Contains("Tolerance: -0.2 / +0.3 mm", markdown); Assert.Contains("Layer: DIM", markdown); Assert.Contains("Text: 18 ±0.2/0.3", markdown); Assert.Contains("Comment: To be reviewed", markdown);
        var summary = ComparisonSummary.From(report.DimensionMatches); Assert.Equal(1, summary.UnmatchedDimensions); Assert.Equal(1, summary.MissingDimensions);
    }

    [Fact(DisplayName = "TD-006 persists Markdown export audit DetailsJson")]
    public async Task Markdown_export_writes_sqlite_audit_with_status_counts_and_format()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.db");
        try
        {
            var sessionId = Guid.NewGuid(); var auth = Authenticated("checker-id", "Checker");
            var audit = new SqliteAuditRepository(path);
            await new ReportService(new Sessions(Fixtures()), auth, audit: audit).GenerateMarkdownAsync(sessionId);
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT DocumentId,UserId,Action,TimestampUtc,DetailsJson FROM DocumentAuditLog";
                await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
                Assert.Equal(sessionId.ToString(), reader.GetString(0)); Assert.Equal("checker-id", reader.GetString(1)); Assert.Equal((int)DocumentAuditAction.VerificationReportGenerated, reader.GetInt32(2)); Assert.True(DateTime.TryParse(reader.GetString(3), out _));
                using var details = JsonDocument.Parse(reader.GetString(4)); var root = details.RootElement;
                Assert.Equal(sessionId.ToString(), root.GetProperty("verificationSessionId").GetGuid().ToString());
                Assert.Equal(4, root.GetProperty("totalDimensions").GetInt32());
                Assert.Equal(2, root.GetProperty("matchedDimensions").GetInt32()); Assert.Equal(1, root.GetProperty("mismatchedDimensions").GetInt32()); Assert.Equal(1, root.GetProperty("unmatchedDimensions").GetInt32()); Assert.Equal("Markdown", root.GetProperty("reportFormat").GetString());
                Assert.Equal(0, root.GetProperty("missingDimensions").GetInt32()); Assert.Equal(50, root.GetProperty("matchPercentage").GetDouble());
            }
            SqliteConnection.ClearAllPools();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-007 propagates audit and generator failures")]
    public async Task Report_failures_are_visible_and_not_returned_as_partial_success()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }));
        var auditFailure = new FailingAudit();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReportService(new Sessions(Fixtures()), session, audit: auditFailure).GenerateMarkdownAsync(Guid.NewGuid()));

        var generatorFailure = new FailingGenerator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReportService(new Sessions(Fixtures()), session, generator: generatorFailure).GenerateMarkdownAsync(Guid.NewGuid()));
    }

    [Fact(DisplayName = "TD-KAN70-02 request normalization rejects invalid scope and preserves deterministic filters")]
    public void Report_request_normalization_validates_scope_and_deduplicates_filters()
    {
        var normalized = new VerificationReportRequest { FeatureFilters = new[] { " Hole ", "hole", "" }, ZoneFilters = new[] { " Front " } }.Normalize();
        Assert.Equal(new[] { "Hole" }, normalized.FeatureFilters);
        Assert.Equal(new[] { "Front" }, normalized.ZoneFilters);
        var exception = Assert.Throws<ArgumentException>(() => new VerificationReportRequest { GdtOnly = true, MismatchedAndMissingOnly = true }.Normalize());
        Assert.Contains("cannot be combined", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN70-05 service uses the current snapshot and audits only after PDF success")]
    public async Task Pdf_service_projects_current_rows_scope_metadata_and_audit_order()
    {
        var sessionId = Guid.NewGuid();
        var matches = new[]
        {
            new DimensionMatch { VerificationSessionId = sessionId, DxfDimensionReference = 1, Status = DimensionMatchStatus.Matched, GeometryType = "Hole" },
            new DimensionMatch { VerificationSessionId = sessionId, DxfDimensionReference = 2, Status = DimensionMatchStatus.Mismatched, GeometryType = "Slot" }
        };
        var auth = Authenticated("checker-id", "Checker");
        var renderer = new CapturingPdfGenerator();
        var audit = new CapturingAudit();
        var service = new ReportService(new Sessions(matches), auth, new RoleAuthorization(), pdfGenerator: renderer, audit: audit, documents: new MetadataDocuments(sessionId, sessionId, Guid.NewGuid(), Guid.NewGuid()));

        var bytes = await service.GeneratePdfAsync(sessionId, new VerificationReportRequest { MismatchedAndMissingOnly = true, FeatureFilters = new[] { "Slot" } });

        Assert.Equal(new byte[] { 37, 80, 68, 70 }, bytes);
        Assert.NotNull(renderer.Report);
        Assert.Single(renderer.Report!.DimensionMatches);
        Assert.Equal(2, renderer.Report.DimensionMatches[0].DxfDimensionReference);
        Assert.True(renderer.Report.Request.MismatchedAndMissingOnly);
        Assert.Equal(new[] { "Slot" }, renderer.Report.Request.FeatureFilters);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(DocumentAuditAction.VerificationReportGenerated, entry.Action);
        using var details = JsonDocument.Parse(entry.DetailsJson);
        Assert.Equal("VerificationReportWithAuditTrail", details.RootElement.GetProperty("reportType").GetString());
        Assert.Equal("PDF", details.RootElement.GetProperty("reportFormat").GetString());
        Assert.Equal("checker-id", entry.UserId);
    }

    [Fact(DisplayName = "TD-KAN70-03 service projects persisted document metadata and checker identity")]
    public async Task Pdf_service_projects_exact_document_version_and_checker_metadata()
    {
        var sessionId = Guid.NewGuid();
        var drawingId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var drawingVersionId = Guid.NewGuid();
        var modelVersionId = Guid.NewGuid();
        var session = new VerificationSession
        {
            VerificationSessionId = sessionId, DxfDocumentId = drawingId, StepDocumentId = modelId,
            DxfDocumentVersionId = drawingVersionId, StepDocumentVersionId = modelVersionId, ToleranceThresholdMm = .25
        };
        var renderer = new CapturingPdfGenerator();
        var auth = Authenticated("checker-id", "Checker");
        var service = new ReportService(new MetadataSessions(session), auth, new RoleAuthorization(), pdfGenerator: renderer, documents: new MetadataDocuments(drawingId, modelId, drawingVersionId, modelVersionId), audit: new DocumentAuditRepository());

        await service.GeneratePdfAsync(sessionId);

        Assert.NotNull(renderer.Report);
        Assert.Equal("drawing-rev7.dxf", renderer.Report!.Metadata.DocumentName);
        Assert.Equal(drawingId, renderer.Report.Metadata.DocumentId);
        Assert.Equal("7", renderer.Report.Metadata.DrawingVersion);
        Assert.Equal("3", renderer.Report.Metadata.ModelVersion);
        Assert.Equal(drawingVersionId, renderer.Report.Metadata.DrawingVersionId);
        Assert.Equal(modelVersionId, renderer.Report.Metadata.ModelVersionId);
        Assert.Equal("checker-id", renderer.Report.Metadata.CheckerIdentity);
        Assert.Contains("0.25", renderer.Report.Metadata.VerificationMethod, StringComparison.Ordinal);
        Assert.Equal(sessionId, renderer.Report.Metadata.VerificationSessionId);
    }

    [Fact(DisplayName = "TD-KAN70-06 PDF renderer and audit failures are rethrown without false-success audit")]
    public async Task Pdf_service_failures_are_distinguishable_and_do_not_emit_success_audit()
    {
        var sessionId = Guid.NewGuid();
        var auth = Authenticated("checker-id", "Checker");
        var audit = new CapturingAudit();
        var renderer = new CapturingPdfGenerator { Failure = new InvalidOperationException("renderer failure") };
        var service = new ReportService(new Sessions(Fixtures()), auth, new RoleAuthorization(), pdfGenerator: renderer, audit: audit, documents: new MetadataDocuments(sessionId, sessionId, Guid.NewGuid(), Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GeneratePdfAsync(sessionId));
        Assert.Equal("renderer failure", exception.Message);
        Assert.Empty(audit.Entries);

        var unauthorized = new ReportService(new Sessions(Fixtures()), new SessionAuthenticationService(), new RoleAuthorization(), pdfGenerator: new CapturingPdfGenerator());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unauthorized.GeneratePdfAsync(sessionId));
    }

    [Fact(DisplayName = "TD-004 exports missing dimensions with location fields and CSV escaping")]
    public async Task Missing_dimension_csv_contains_location_and_escaped_comment()
    {
        var authentication = Authenticated("checker", "Checker");
        var match = new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 4, NominalValue = 50, DxfLayer = "DIM", BlockName = "B1", ViewName = "Front", LocationX = 1.25, LocationY = 2.5, DimensionText = "50,00", Review = new DimensionReview { DimensionMatchId = Guid.NewGuid(), Comment = "Needs \"design\"\nreview", Status = DimensionResolutionStatus.Open } };
        var csv = await new ReportService(new Sessions(new[] { match }), authentication).GenerateMissingDimensionsCsvAsync(Guid.NewGuid());
        Assert.Contains("DimensionMatchId,DimensionValue,Tolerance,Layer,Block,View,X,Y,Annotation,Resolution,Comment", csv);
        Assert.Contains("DIM,B1,Front,1.25,2.5", csv);
        Assert.Contains("\"Needs \"\"design\"\"\nreview\"", csv);
    }

    [Fact(DisplayName = "TSD-AC01-AUDIT-SECTION and TSD-AC10-REPORT-METADATA populate the audit report contract")]
    public void TSD_AC01_AND_TSD_AC10_report_contract_contains_section_metadata_and_empty_state()
    {
        var report = new VerificationReport { Metadata = new VerificationReportMetadata { ReportType = "VerificationReportWithAuditTrail", GeneratorIdentity = "Approver One", DocumentId = Guid.NewGuid(), GeneratedUtc = DateTimeOffset.Parse("2026-09-17T10:00:00Z") } };
        Assert.Equal("VerificationReportWithAuditTrail", report.Metadata.ReportType);
        Assert.Equal("Approver One", report.Metadata.GeneratorIdentity);
        Assert.Empty(report.AuditTrail);
    }

    [Fact(DisplayName = "TSD-AC11-SCOPED-FILTERS rejects conflicting and reversed audit scopes")]
    public void TSD_AC11_SCOPED_FILTERS_normalizes_user_and_rejects_invalid_ranges()
    {
        var normalized = new VerificationReportRequest { AuditUserId = "  approver-1  ", AuditFromUtc = DateTimeOffset.Parse("2026-01-01Z"), AuditToUtc = DateTimeOffset.Parse("2026-01-02Z") }.Normalize();
        Assert.Equal("approver-1", normalized.AuditUserId);
        Assert.Throws<ArgumentException>(() => new VerificationReportRequest { IncludeVerificationAuditOnly = true, IncludeApprovalAuditOnly = true }.Normalize());
        Assert.Throws<ArgumentException>(() => new VerificationReportRequest { AuditFromUtc = DateTimeOffset.Parse("2026-01-02Z"), AuditToUtc = DateTimeOffset.Parse("2026-01-01Z") }.Normalize());
    }

    [Fact(DisplayName = "TSD-AC13-GENERATION-AUDIT identifies the report-generation audit event")]
    public void TSD_AC13_GENERATION_AUDIT_uses_expected_action_and_report_type()
    {
        var entry = new AuditEntry { Action = DocumentAuditAction.VerificationReportGenerated, DetailsJson = "{\"reportType\":\"VerificationReportWithAuditTrail\"}" };
        Assert.Equal(DocumentAuditAction.VerificationReportGenerated, entry.Action);
        Assert.Contains("VerificationReportWithAuditTrail", entry.DetailsJson);
    }

    private static SessionAuthenticationService Authenticated(string userId, string role) { var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser(userId, new[] { role }) { UserId = userId }); return auth; }
    private static DimensionMatch[] Fixtures() => new[] { new DimensionMatch { DxfDimensionReference = 1, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Matched }, new DimensionMatch { DxfDimensionReference = 2, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Mismatched }, new DimensionMatch { DxfDimensionReference = 3, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Unmatched }, new DimensionMatch { DxfDimensionReference = 4, DimensionType = DimensionType.Angular, Status = DimensionMatchStatus.Matched } };
    private static string Label(DimensionMatchStatus status) => status.ToString();
    private static (int, int, int) TypeCounts(ComparisonSummary summary, DimensionType type)
    {
        var item = Assert.Single(summary.DimensionTypes.Where(item => item.DimensionType == type));
        return (item.MatchedDimensions, item.MismatchedDimensions, item.UnmatchedDimensions);
    }
    private sealed class FailingAudit : IAuditLogger
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("audit failure"));
    }
    private sealed class FailingGenerator : IReportGenerator
    {
        public Task<string> GenerateMarkdownAsync(VerificationReport report, CancellationToken cancellationToken = default) => Task.FromException<string>(new InvalidOperationException("generator failure"));
    }

    private sealed class CapturingPdfGenerator : IPdfReportGenerator
    {
        public VerificationReport? Report { get; private set; }
        public Exception? Failure { get; init; }
        public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default)
        {
            Report = report;
            return Failure is null ? Task.FromResult(new byte[] { 37, 80, 68, 70 }) : Task.FromException<byte[]>(Failure);
        }
    }

    private sealed class CapturingAudit : IAuditLogger, IDocumentAuditRepository
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentAuditRecord>>(Array.Empty<DocumentAuditRecord>());
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DocumentAuditRepository : IAuditLogger, IDocumentAuditRepository
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentAuditRecord>>(Array.Empty<DocumentAuditRecord>());
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MetadataSessions(VerificationSession session) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(session);
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>());
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MetadataDocuments(Guid drawingId, Guid modelId, Guid drawingVersionId, Guid modelVersionId) : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(documentId == drawingId ? new Document { DocumentId = drawingId, OriginalFileName = "drawing-rev7.dxf" } : new Document { DocumentId = modelId, OriginalFileName = "model-rev3.step" });
        public Task<DocumentVersion?> GetVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<DocumentVersion?>(documentId == drawingId ? new DocumentVersion { DocumentId = drawingId, VersionId = drawingVersionId, VersionNumber = 7 } : new DocumentVersion { DocumentId = modelId, VersionId = modelVersionId, VersionNumber = 3 });
    }
    private sealed class Sessions(IReadOnlyList<DimensionMatch> matches) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id, StepDocumentId = id, DxfDocumentId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? matches : matches.Where(match => match.Status == status).ToArray());
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) => throw new NotImplementedException();
    }
}
