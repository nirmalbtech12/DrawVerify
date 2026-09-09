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
    [Fact(DisplayName = "TD-SVC-001 generates complete canonical status summary and details")]
    public async Task Markdown_report_contains_every_dimension_and_derived_counts()
    {
        var sessionId = Guid.NewGuid();
        var matches = Fixtures();
        var auth = Authenticated("checker", "Checker");
        var content = await new ReportService(new Sessions(matches), auth).GenerateMarkdownAsync(sessionId);

        Assert.Contains("## Dimension Status Summary", content);
        Assert.Contains("Linear: 1 Matched, 1 Mismatched, 1 Unmatched", content);
        Assert.Contains("Angular: 1 Matched, 0 Mismatched, 0 Unmatched", content);
        foreach (var match in matches)
        {
            Assert.Contains($"`{match.DxfDimensionReference}`", content);
            Assert.Contains($"Status: {Label(match.Status)}", content);
        }
    }

    [Fact(DisplayName = "TD-SVC-002 persists Markdown export audit details")]
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
                Assert.Equal(2, root.GetProperty("matchedDimensions").GetInt32()); Assert.Equal(1, root.GetProperty("mismatchedDimensions").GetInt32()); Assert.Equal(1, root.GetProperty("unmatchedDimensions").GetInt32()); Assert.Equal("Markdown", root.GetProperty("reportFormat").GetString());
            }
            SqliteConnection.ClearAllPools();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static SessionAuthenticationService Authenticated(string userId, string role) { var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser(userId, new[] { role }) { UserId = userId }); return auth; }
    private static DimensionMatch[] Fixtures() => new[] { new DimensionMatch { DxfDimensionReference = 1, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Matched }, new DimensionMatch { DxfDimensionReference = 2, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Mismatched }, new DimensionMatch { DxfDimensionReference = 3, DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Unmatched }, new DimensionMatch { DxfDimensionReference = 4, DimensionType = DimensionType.Angular, Status = DimensionMatchStatus.Matched } };
    private static string Label(DimensionMatchStatus status) => status.ToString();
    private sealed class Sessions(IReadOnlyList<DimensionMatch> matches) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? matches : matches.Where(match => match.Status == status).ToArray());
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) => throw new NotImplementedException();
    }
}
