namespace DrawVerify.Application.Tests;

using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

/// <summary>
/// Tests for the verification orchestration pipeline. Must run WITHOUT
/// physical files, DB, or WPF — geometry supplied via a fake IGeometryService.
/// </summary>
public class VerificationServiceTests
{
    [Fact(DisplayName = "TD-APP-001 generates Markdown delta content and audit metadata for Checker")]
    public async Task Authorized_report_contains_deltas_and_writes_export_audit()
    {
        var sessionId = Guid.NewGuid(); var user = new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker-id" };
        var sessions = new ReportSessions(new[] { new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 7, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.85 } });
        var auth = new SessionAuthenticationService(); auth.SignIn(user); var audit = new AuditFake();
        var content = await new ReportService(sessions, auth, audit: audit).GenerateMarkdownAsync(sessionId);
        Assert.Contains("Mismatched Dimensions with Delta", content); Assert.Contains("+0.15", content);
        var entry = Assert.Single(audit.Entries); Assert.Equal(sessionId, entry.DocumentId); Assert.Equal("checker-id", entry.UserId); Assert.Equal(DocumentAuditAction.VerificationReportGenerated, entry.Action);
        using var details = JsonDocument.Parse(entry.DetailsJson); Assert.Equal(sessionId, details.RootElement.GetProperty("verificationSessionId").GetGuid()); Assert.Equal(1, details.RootElement.GetProperty("mismatchedDimensions").GetInt32()); Assert.Equal("Markdown", details.RootElement.GetProperty("reportFormat").GetString());
    }

    [Theory(DisplayName = "TD-APP-002 allows only Checker and Maker report access")]
    [InlineData(null, false)] [InlineData("Viewer", false)] [InlineData("Checker", true)] [InlineData("Maker", true)]
    public async Task Report_access_matches_existing_permission(string? role, bool allowed)
    {
        var auth = new SessionAuthenticationService(); if (role is not null) auth.SignIn(new AuthenticatedUser("user", new[] { role }));
        var task = new ReportService(new ReportSessions(Array.Empty<DimensionMatch>()), auth).GenerateMarkdownAsync(Guid.NewGuid());
        if (allowed) Assert.Contains("Verification Report", await task); else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
    }

    [Fact(DisplayName = "TD-APP-003 propagates cancellation and audit-write failures with logging")]
    public async Task Report_failures_are_explicit_and_logged()
    {
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }));
        var logger = new LoggerFake(); var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReportService(new ReportSessions(Array.Empty<DimensionMatch>()), auth, logger: logger).GenerateMarkdownAsync(Guid.NewGuid(), cancel.Token));
        var failureAudit = new AuditFake { ThrowOnWrite = true }; var service = new ReportService(new ReportSessions(Array.Empty<DimensionMatch>()), auth, logger: logger, audit: failureAudit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateMarkdownAsync(Guid.NewGuid())); Assert.NotEmpty(logger.Exceptions);
    }

    private sealed class ReportSessions(IReadOnlyList<DimensionMatch> matches) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult(status is null ? matches : matches.Where(x => x.Status == status).ToArray() as IReadOnlyList<DimensionMatch>);
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class AuditFake : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public bool ThrowOnWrite { get; init; } public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { if (ThrowOnWrite) return Task.FromException(new InvalidOperationException("audit unavailable")); Entries.Add(entry); return Task.CompletedTask; } }
    private sealed class LoggerFake : ICentralizedLogger { public List<Exception> Exceptions { get; } = new(); public void Log(Exception exception, string operation, string context) => Exceptions.Add(exception); }
}
