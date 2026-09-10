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
    private sealed class Sessions(IReadOnlyList<DimensionMatch> matches) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? matches : matches.Where(match => match.Status == status).ToArray());
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) => throw new NotImplementedException();
    }
}
