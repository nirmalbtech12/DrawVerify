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
    private readonly IDocumentRepository? _documents;
    private readonly IUserRepository? _users;
    public ReportService(IVerificationSessionRepository sessions, ISessionAuthenticationService authentication, RoleAuthorization? authorization = null, IReportGenerator? generator = null, ICentralizedLogger? logger = null, IAuditLogger? audit = null, IPdfReportGenerator? pdfGenerator = null, IWorkflowRepository? workflow = null, IDocumentRepository? documents = null, IUserRepository? users = null)
    { _sessions = sessions; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _generator = generator ?? new MarkdownReportGenerator(); _logger = logger; _audit = audit; _pdfGenerator = pdfGenerator; _workflow = workflow; _documents = documents; _users = users; }
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
        => await GeneratePdfAsync(sessionId, new VerificationReportRequest(), cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> GeneratePdfAsync(Guid sessionId, VerificationReportRequest request, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Only authenticated Maker, Checker, or Approver users may access verification reports.");
        if (_pdfGenerator is null) throw new InvalidOperationException("PDF report generation is not configured.");
        try
        {
            var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var gdtResults = await _sessions.GetGdtResultsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The verification session was not found.");
            var normalized = (request ?? throw new ArgumentNullException(nameof(request))).Normalize();
            var filteredMatches = matches.Where(x => !normalized.GdtOnly).Where(x => !normalized.MismatchedAndMissingOnly || x.Status is DimensionMatchStatus.Mismatched or DimensionMatchStatus.Unmatched).Where(x => normalized.FeatureFilters.Count == 0 || normalized.FeatureFilters.Contains(x.GeometryType ?? string.Empty, StringComparer.OrdinalIgnoreCase)).Where(x => normalized.ZoneFilters.Count == 0 || normalized.ZoneFilters.Contains(x.ViewName ?? string.Empty, StringComparer.OrdinalIgnoreCase)).ToArray();
            var filteredGdt = normalized.MismatchedAndMissingOnly ? Array.Empty<GdtResult>() : gdtResults.Where(x => !normalized.GdtOnly || normalized.GdtOnly).Where(x => normalized.FeatureFilters.Count == 0 || normalized.FeatureFilters.Contains(x.ControlledFeature ?? string.Empty, StringComparer.OrdinalIgnoreCase)).Where(x => normalized.ZoneFilters.Count == 0 || normalized.ZoneFilters.Contains(x.ViewName ?? string.Empty, StringComparer.OrdinalIgnoreCase)).ToArray();
            var report = await BuildReportAsync(sessionId, filteredMatches, filteredGdt, cancellationToken).ConfigureAwait(false);
            var metadata = await BuildMetadataAsync(session, cancellationToken).ConfigureAwait(false);
            var auditTrail = await BuildAuditTrailAsync(metadata.DocumentId, normalized, metadata.GeneratedUtc, cancellationToken).ConfigureAwait(false);
            metadata = metadata with { AuditScope = DescribeAuditScope(normalized) };
            report = report with { Metadata = metadata, Request = normalized, AuditTrail = auditTrail };
            var content = await _pdfGenerator.GeneratePdfAsync(report, normalized, cancellationToken).ConfigureAwait(false);
            if (_audit is not null)
            {
                var summary = report.ComparisonSummary;
                var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, documentId = report.Metadata.DocumentId, drawingVersionId = report.Metadata.DrawingVersionId, modelVersionId = report.Metadata.ModelVersionId, reportType = report.Metadata.ReportType, totalDimensions = summary.TotalDimensions, matchedDimensions = summary.MatchedDimensions, mismatchedDimensions = summary.MismatchedDimensions, unmatchedDimensions = summary.UnmatchedDimensions, missingDimensions = summary.MissingDimensions, extraDimensions = summary.ExtraDimensions, notVerifiedGdtControls = summary.NotVerifiedGdtControls, matchPercentage = summary.MatchPercentage, reportFormat = "PDF", generatedUtc = report.Metadata.GeneratedUtc, scope = normalized });
                await _audit.WriteAsync(new AuditEntry { DocumentId = report.Metadata.DocumentId ?? sessionId, UserId = _authentication.CurrentUser!.UserId, ActorRole = _authentication.CurrentUser.Roles.FirstOrDefault(), DocumentRevision = report.Metadata.DrawingVersion, DocumentVersion = report.Metadata.ModelVersion, Action = DocumentAuditAction.VerificationReportGenerated, TimestampUtc = report.Metadata.GeneratedUtc.UtcDateTime, DetailsJson = details }, cancellationToken).ConfigureAwait(false);
            }
            return content;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        { _logger?.Log(exception, "Verification PDF report generation", $"Unable to generate PDF report for verificationSessionId={sessionId}."); throw; }
    }

    private async Task<VerificationReportMetadata> BuildMetadataAsync(VerificationSession session, CancellationToken cancellationToken)
    {
        var drawing = _documents is null ? null : await _documents.GetAsync(session.DxfDocumentId, cancellationToken).ConfigureAwait(false);
        var model = _documents is null ? null : await _documents.GetAsync(session.StepDocumentId, cancellationToken).ConfigureAwait(false);
        var drawingVersion = session.DxfDocumentVersionId.HasValue && _documents is not null ? await _documents.GetVersionAsync(session.DxfDocumentId, session.DxfDocumentVersionId.Value, cancellationToken).ConfigureAwait(false) : null;
        var modelVersion = session.StepDocumentVersionId.HasValue && _documents is not null ? await _documents.GetVersionAsync(session.StepDocumentId, session.StepDocumentVersionId.Value, cancellationToken).ConfigureAwait(false) : null;
        return new VerificationReportMetadata { DocumentName = drawing?.OriginalFileName ?? model?.OriginalFileName ?? "Unavailable", DocumentId = drawing?.DocumentId ?? model?.DocumentId, DrawingVersion = drawingVersion?.VersionNumber.ToString(CultureInfo.InvariantCulture) ?? "Unavailable", ModelVersion = modelVersion?.VersionNumber.ToString(CultureInfo.InvariantCulture) ?? "Unavailable", DrawingVersionId = drawingVersion?.VersionId, ModelVersionId = modelVersion?.VersionId, VerificationSessionId = session.VerificationSessionId, VerificationMethod = session.ToleranceThresholdMm is null ? "Persisted verification configuration unavailable" : $"Tolerance threshold: {session.ToleranceThresholdMm.Value.ToString(CultureInfo.InvariantCulture)} mm", CheckerIdentity = _authentication.CurrentUser?.DisplayName, GeneratorIdentity = _authentication.CurrentUser?.DisplayName ?? "Unavailable", GeneratedUtc = DateTimeOffset.UtcNow };
    }

    private async Task<IReadOnlyList<AuditTrailEntry>> BuildAuditTrailAsync(Guid? documentId, VerificationReportRequest request, DateTimeOffset snapshotTime, CancellationToken cancellationToken)
    {
        if (_audit is not IDocumentAuditRepository repository)
        {
            var exception = new InvalidOperationException("The document audit repository is required to generate a verification report with an audit trail.");
            _logger?.Log(exception, "Audit trail snapshot", "The configured audit service does not support document audit queries.");
            throw exception;
        }
        if (documentId is not { } id || id == Guid.Empty)
        {
            var exception = new InvalidOperationException("A non-empty document identity is required to generate a verification report with an audit trail.");
            _logger?.Log(exception, "Audit trail snapshot", "The verification report metadata did not contain a document identity.");
            throw exception;
        }
        await repository.VerifyIntegrityAsync(id, cancellationToken).ConfigureAwait(false);
        var toUtc = request.AuditToUtc is { } requestedTo && requestedTo < snapshotTime ? requestedTo : snapshotTime;
        var records = new List<DocumentAuditRecord>();
        for (var page = 1; ; page++)
        {
            var batch = await repository.QueryAsync(new AuditLogQuery(DocumentId: id, FromUtc: request.AuditFromUtc, ToUtc: toUtc, ActorUsername: request.AuditUserId, Page: page, PageSize: 1000), cancellationToken).ConfigureAwait(false);
            records.AddRange(batch);
            if (batch.Count < 1000) break;
        }
        var users = _users is null ? Array.Empty<UserAccount>() : await _users.ListAsync(cancellationToken).ConfigureAwait(false);
        var verification = new[] { DocumentAuditAction.VerificationRunCompleted, DocumentAuditAction.VerificationRunFailed, DocumentAuditAction.DimensionsMatched, DocumentAuditAction.DimensionsMatchedPartial, DocumentAuditAction.GdtValidationCompleted, DocumentAuditAction.VerificationReportGenerated };
        var approval = new[] { DocumentAuditAction.MakerSignatureApplied, DocumentAuditAction.CheckerSignatureApplied, DocumentAuditAction.ApproverSignatureApplied, DocumentAuditAction.InternalApprovalStampApplied, DocumentAuditAction.FinalApproved, DocumentAuditAction.Approved, DocumentAuditAction.Rejected };
        return records.Where(record => !request.IncludeVerificationAuditOnly || verification.Contains(record.Action)).Where(record => !request.IncludeApprovalAuditOnly || approval.Contains(record.Action)).OrderBy(record => record.Timestamp).ThenBy(record => record.AuditId).Select(record => ProjectAudit(record, users)).ToArray();
    }

    private AuditTrailEntry ProjectAudit(DocumentAuditRecord record, IReadOnlyList<UserAccount> users)
    {
        var user = users.FirstOrDefault(item => string.Equals(item.Id.ToString(), record.ActorId, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Username, record.ActorId, StringComparison.OrdinalIgnoreCase));
        var details = record.DetailsJson;
        var outcome = record.NewWorkflowStatus ?? "Success";
        var identifier = record.DocumentVersion ?? record.DocumentRevision ?? "Unavailable";
        try
        {
            using var json = JsonDocument.Parse(record.DetailsJson);
            var root = json.RootElement;
            foreach (var name in new[] { "outcome", "status", "result" }) if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) outcome = value.GetString()!;
            foreach (var name in new[] { "verificationRunId", "runId", "verificationSessionId", "submissionId" }) if (root.TryGetProperty(name, out var value)) { identifier = value.ToString(); break; }
            details = root.ToString();
        }
        catch (JsonException exception) { _logger?.Log(exception, "Audit trail projection", $"Malformed details for auditId={record.AuditId}."); throw new InvalidDataException($"Audit details are invalid for audit entry {record.AuditId}.", exception); }
        return new AuditTrailEntry { AuditId = record.AuditId, DocumentId = record.DocumentId, DocumentRevision = record.DocumentRevision, DocumentVersion = record.DocumentVersion, UserId = record.ActorId, UserName = user?.DisplayName ?? (_authentication.CurrentUser?.UserId == record.ActorId ? _authentication.CurrentUser.DisplayName : "Unavailable"), UserRole = user?.Roles.FirstOrDefault() ?? record.ActorRole ?? "Unavailable", UserEmail = user?.Email, Timestamp = record.Timestamp, Action = record.Action, Outcome = outcome, Details = details, Identifier = identifier, IntegrityReference = record.IntegrityHash };
    }
    private static string DescribeAuditScope(VerificationReportRequest request) => request.IncludeVerificationAuditOnly ? "Verification actions" : request.IncludeApprovalAuditOnly ? "Approval and signature actions" : "Complete audit trail";

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
        if (submission?.CheckerSignature is not null) submission.CheckerSignature.Validate();
        if (submission?.ApproverSignature is not null)
        {
            try
            {
                submission.ApproverSignature.Validate();
                if (!submission.ApproverSignature.IsApprover || submission.ApproverSignature.DocumentId != submission.PackageId || submission.ApproverSignature.Revision != submission.Revision)
                    throw new InvalidDataException("Persisted Approver signature identity or revision is invalid.");
            }
            catch (Exception exception)
            {
                _logger?.Log(exception, "Approver report projection", $"Persisted Approver signature validation failed for packageId={submission.PackageId}, revision={submission.Revision}.");
                throw;
            }
        }
        var verification = submission is null ? null : await _workflow!.GetFingerprintVerificationAsync(submission.PackageId, submission.Revision, cancellationToken).ConfigureAwait(false);
        var stamp = submission?.InternalApprovalStamp;
        if (stamp is not null && verification is not null) stamp = stamp with { VerificationStatus = verification.IsMatch ? InternalApprovalStampVerificationStatus.Valid : InternalApprovalStampVerificationStatus.Invalid };
        return new VerificationReport { DimensionMatches = matches, GdtResults = gdtResults, Fingerprint = fingerprint, FingerprintVerification = verification, MakerSignature = submission?.MakerSignature, CheckerSignature = submission?.CheckerSignature, ApproverSignature = submission?.ApproverSignature, InternalApprovalStamp = stamp };
    }
    private static string Escape(string? value) { value ??= string.Empty; return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value; }
}
