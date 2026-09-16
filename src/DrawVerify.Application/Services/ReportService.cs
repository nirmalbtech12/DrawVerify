namespace DrawVerify.Application.Services;
using System.Text.Json;
using System.Globalization;
using System.Text;
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
    private readonly IPdfReportGenerator? _pdfGenerator;
    private readonly IWorkflowRepository? _workflow;
    private readonly ICentralizedLogger? _logger;
    private readonly IAuditLogger? _audit;
    public ReportService(IVerificationSessionRepository sessions, ISessionAuthenticationService authentication, RoleAuthorization? authorization = null, IReportGenerator? generator = null, ICentralizedLogger? logger = null, IAuditLogger? audit = null, IPdfReportGenerator? pdfGenerator = null, IWorkflowRepository? workflow = null)
    { _sessions = sessions; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _generator = generator ?? new MarkdownReportGenerator(); _logger = logger; _audit = audit; _pdfGenerator = pdfGenerator; _workflow = workflow; }
    public async Task<string> GenerateMarkdownAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Only authenticated Maker, Checker, or Approver users may access verification reports.");
        try
        {
            var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var gdtResults = await _sessions.GetGdtResultsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var report = await BuildReportAsync(sessionId, matches, gdtResults, cancellationToken).ConfigureAwait(false);
            var content = await _generator.GenerateMarkdownAsync(report, cancellationToken).ConfigureAwait(false);
            if (_audit is not null)
            {
                var summary = report.ComparisonSummary;
                var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, totalDimensions = summary.TotalDimensions, matchedDimensions = summary.MatchedDimensions, mismatchedDimensions = summary.MismatchedDimensions, unmatchedDimensions = summary.UnmatchedDimensions, missingDimensions = summary.MissingDimensions, extraDimensions = summary.ExtraDimensions, notVerifiedGdtControls = summary.NotVerifiedGdtControls, matchPercentage = summary.MatchPercentage, reportFormat = "Markdown" });
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

    public async Task<byte[]> GeneratePdfAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Only authenticated Maker, Checker, or Approver users may access verification reports.");
        if (_pdfGenerator is null) throw new InvalidOperationException("PDF report generation is not configured.");
        try
        {
            var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var gdtResults = await _sessions.GetGdtResultsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var report = await BuildReportAsync(sessionId, matches, gdtResults, cancellationToken).ConfigureAwait(false);
            var content = await _pdfGenerator.GeneratePdfAsync(report, cancellationToken).ConfigureAwait(false);
            if (_audit is not null)
            {
                var summary = report.ComparisonSummary;
                var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, totalDimensions = summary.TotalDimensions, matchedDimensions = summary.MatchedDimensions, mismatchedDimensions = summary.MismatchedDimensions, unmatchedDimensions = summary.UnmatchedDimensions, missingDimensions = summary.MissingDimensions, extraDimensions = summary.ExtraDimensions, notVerifiedGdtControls = summary.NotVerifiedGdtControls, matchPercentage = summary.MatchPercentage, reportFormat = "PDF" });
                await _audit.WriteAsync(new AuditEntry { DocumentId = sessionId, UserId = _authentication.CurrentUser!.UserId, Action = DocumentAuditAction.VerificationReportGenerated, TimestampUtc = DateTime.UtcNow, DetailsJson = details }, cancellationToken).ConfigureAwait(false);
            }
            return content;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        { _logger?.Log(exception, "Verification PDF report generation", $"Unable to generate PDF report for verificationSessionId={sessionId}."); throw; }
    }

    public async Task<string> GenerateMissingDimensionsCsvAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.GenerateFinalReports)) throw new UnauthorizedAccessException("Only authorized reviewers may export verification results.");
        try
        {
            var matches = await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.Unmatched, cancellationToken).ConfigureAwait(false);
            var builder = new StringBuilder();
            builder.AppendLine("DimensionMatchId,DimensionValue,Tolerance,Layer,Block,View,X,Y,Annotation,Resolution,Comment");
            foreach (var m in matches)
                builder.AppendLine(string.Join(',', new[] { m.DimensionMatchId.ToString(), Value(m.NominalValue), Value(m.EffectiveLowerTolerance) + (m.EffectiveUpperTolerance is null ? string.Empty : "/+" + Value(m.EffectiveUpperTolerance)), m.DxfLayer, m.BlockName, m.ViewName, Value(m.LocationX), Value(m.LocationY), m.DimensionText, m.Review?.Status.ToString(), m.Review?.Comment }.Select(Escape)));
            return builder.ToString();
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        { _logger?.Log(exception, "Missing dimension CSV export", $"Unable to export missing dimensions for verificationSessionId={sessionId}."); throw; }
    }
    public async Task<string> GenerateExtraDimensionsCsvAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.GenerateFinalReports)) throw new UnauthorizedAccessException("Only authorized reviewers may export verification results.");
        var matches = (await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false)).Where(item => DimensionMatchingService.IsExtraDimensionStatus(item.Status)).ToArray();
        var builder = new StringBuilder("DimensionMatchId,StepReference,GeometryType,Value,Tolerance,View,X,Y,Comment,DesignUpdate\n");
        foreach (var m in matches)
            builder.AppendLine(string.Join(',', new[] { m.DimensionMatchId.ToString(), m.StepGeometryReference, m.GeometryType, Value(m.MeasuredValue ?? m.NominalValue), Value(m.EffectiveLowerTolerance) + (m.EffectiveUpperTolerance is null ? string.Empty : "/+" + Value(m.EffectiveUpperTolerance)), m.ViewName, Value(m.LocationX), Value(m.LocationY), m.Review?.Comment, m.Review?.RequiresDesignUpdate.ToString() }.Select(Escape)));
        return builder.ToString();
    }
    private static string Value(double? value) => value?.ToString("0.############", CultureInfo.InvariantCulture) ?? string.Empty;
    private async Task<VerificationReport> BuildReportAsync(Guid sessionId, IReadOnlyList<DimensionMatch> matches, IReadOnlyList<GdtResult> gdtResults, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The verification session was not found.");
        var submissions = _workflow is null ? Array.Empty<ReviewSubmission>() : await _workflow.GetSubmissionsAsync(Guid.Empty, cancellationToken).ConfigureAwait(false);
        var submission = submissions.Where(x => x.VerificationSessionId == sessionId)
            .Where(x => !session.StepDocumentVersionId.HasValue || x.StepDocumentVersionId == session.StepDocumentVersionId.Value)
            .Where(x => !session.DxfDocumentVersionId.HasValue || x.DxfDocumentVersionId == session.DxfDocumentVersionId.Value)
            .OrderByDescending(x => x.Revision).ThenByDescending(x => x.SubmittedUtc).FirstOrDefault();
        var fingerprint = submission?.Fingerprint;
        if (submission?.MakerSignature is not null) submission.MakerSignature.Validate();
        var verification = submission is null ? null : await _workflow!.GetFingerprintVerificationAsync(submission.PackageId, submission.Revision, cancellationToken).ConfigureAwait(false);
        return new VerificationReport { DimensionMatches = matches, GdtResults = gdtResults, Fingerprint = fingerprint, FingerprintVerification = verification, MakerSignature = submission?.MakerSignature, CheckerSignature = submission?.CheckerSignature };
    }
    private static string Escape(string? value) { value ??= string.Empty; return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value; }
}
