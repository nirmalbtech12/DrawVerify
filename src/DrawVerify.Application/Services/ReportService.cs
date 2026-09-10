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
                var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, totalDimensions = summary.TotalDimensions, matchedDimensions = summary.MatchedDimensions, mismatchedDimensions = summary.MismatchedDimensions, unmatchedDimensions = summary.UnmatchedDimensions, missingDimensions = summary.MissingDimensions, missingIn3DCount = summary.MissingIn3DCount, extraDimensionCount = summary.ExtraDimensionCount, matchPercentage = summary.MatchPercentage, reportFormat = "Markdown" });
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

    public async Task<string> GenerateMissingDimensionsCsvAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.GenerateFinalReports)) throw new UnauthorizedAccessException("Only authorized reviewers may export verification results.");
        try
        {
            var matches = (await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.Unmatched, cancellationToken).ConfigureAwait(false)).Concat(await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.ExtraDimension, cancellationToken).ConfigureAwait(false)).ToArray();
            var builder = new StringBuilder();
            builder.AppendLine("DimensionMatchId,DimensionValue,Tolerance,Layer,Block,View,X,Y,Annotation,Resolution,Comment,Status,DimensionValue,FeatureReference,GeometryType,Z");
            foreach (var m in matches)
                builder.AppendLine(string.Join(',', new[] { m.DimensionMatchId.ToString(), Value(m.Status == DimensionMatchStatus.ExtraDimension ? m.FeatureValue : m.NominalValue), Value(m.EffectiveLowerTolerance) + (m.EffectiveUpperTolerance is null ? string.Empty : "/+" + Value(m.EffectiveUpperTolerance)), m.DxfLayer, m.BlockName, m.ViewName, Value(m.Status == DimensionMatchStatus.ExtraDimension ? m.StepLocationX : m.LocationX), Value(m.Status == DimensionMatchStatus.ExtraDimension ? m.StepLocationY : m.LocationY), m.DimensionText, m.Review?.Status.ToString(), m.Review?.Comment, m.Status.ToString(), Value(m.Status == DimensionMatchStatus.ExtraDimension ? m.FeatureValue : m.NominalValue), m.StepGeometryReference, m.GeometryType, Value(m.StepLocationZ) }.Select(Escape)));
            return builder.ToString();
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        { _logger?.Log(exception, "Missing dimension CSV export", $"Unable to export missing dimensions for verificationSessionId={sessionId}."); throw; }
    }
    private static string Value(double? value) => value?.ToString("0.############", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Escape(string? value) { value ??= string.Empty; return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value; }
}
