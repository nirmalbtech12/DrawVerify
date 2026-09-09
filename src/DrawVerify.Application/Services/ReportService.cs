namespace DrawVerify.Application.Services;
using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Builds verification report content; delegates rendering to IReportGenerator.
/// </summary>
public sealed class ReportService
{
    private readonly IVerificationSessionRepository _sessions;
    private readonly ISessionAuthenticationService _authentication;
    private readonly RoleAuthorization _authorization;
    private readonly IReportGenerator _generator;
    private readonly ICentralizedLogger? _logger;
    private readonly IAuditLogger? _audit;
    public ReportService(IVerificationSessionRepository sessions, ISessionAuthenticationService authentication, RoleAuthorization? authorization = null, IReportGenerator? generator = null, ICentralizedLogger? logger = null, IAuditLogger? audit = null)
    { _sessions = sessions; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _generator = generator ?? new MarkdownReportGenerator(); _logger = logger; _audit = audit; }
    public async Task<string> GenerateMarkdownAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Only authenticated Maker, Checker, or Approver users may access verification reports.");
        try
        {
            var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var report = new VerificationReport { DimensionMatches = matches };
            var content = await _generator.GenerateMarkdownAsync(report, cancellationToken).ConfigureAwait(false);
            if (_audit is not null)
            {
                var summary = report.ComparisonSummary;
                var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, totalDimensions = summary.TotalDimensions, matchedDimensions = summary.MatchedDimensions, mismatchedDimensions = summary.MismatchedDimensions, unmatchedDimensions = summary.UnmatchedDimensions, missingDimensions = summary.MissingDimensions, matchPercentage = summary.MatchPercentage, reportFormat = "Markdown" });
                await _audit.WriteAsync(new AuditEntry { DocumentId = sessionId, UserId = _authentication.CurrentUser!.UserId, Action = DocumentAuditAction.VerificationReportGenerated, TimestampUtc = DateTime.UtcNow, DetailsJson = details }, cancellationToken).ConfigureAwait(false);
            }
            return content;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        {
            _logger?.Log(exception, "Verification report generation", $"Unable to generate Markdown report for verificationSessionId={sessionId}.");
            throw;
        }
    }
}
