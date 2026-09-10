using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Application.Verification;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public sealed class DimensionMatchingService
{
    private readonly IDocumentRepository _documents;
    private readonly IVerificationSessionRepository _sessions;
    private readonly IMatchableGeometryService _geometry;
    private readonly ISessionAuthenticationService _authentication;
    private readonly RoleAuthorization _authorization;
    private readonly DimensionMatchingConfiguration _configuration;
    private readonly ICentralizedLogger? _logger;
    public DimensionMatchingService(IDocumentRepository documents, IVerificationSessionRepository sessions, IMatchableGeometryService geometry, ISessionAuthenticationService authentication, DimensionMatchingConfiguration configuration, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null)
    { _documents = documents; _sessions = sessions; _geometry = geometry; _authentication = authentication; _configuration = configuration; _configuration.Validate(); _authorization = authorization ?? new RoleAuthorization(); _logger = logger; }

    public async Task<VerificationSession> MatchDimensionsToGeometryAsync(Guid sessionId, CancellationToken cancellationToken = default, double? toleranceOverrideMm = null)
    {
        EnsureAuthorized(RolePermission.MatchDimensionsToGeometry);
        var started = Stopwatch.GetTimestamp();
        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Verification session was not found.");
        if (toleranceOverrideMm is not null && (!double.IsFinite(toleranceOverrideMm.Value) || toleranceOverrideMm.Value < 0)) throw new ArgumentOutOfRangeException(nameof(toleranceOverrideMm));
        var effectiveToleranceThresholdMm = toleranceOverrideMm ?? session.ToleranceThresholdMm;
        if (effectiveToleranceThresholdMm is not null) session = session with { ToleranceThresholdMm = effectiveToleranceThresholdMm };
        Guid? stepDocumentVersionId = session.StepDocumentVersionId;
        Guid? dxfDocumentVersionId = session.DxfDocumentVersionId;
        using var comparisonTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        comparisonTimeout.CancelAfter(TimeSpan.FromSeconds(_configuration.ComparisonTimeoutSeconds));
        cancellationToken = comparisonTimeout.Token;
        try
        {
            var step = await _documents.GetAsync(session.StepDocumentId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("STEP document was not found.");
            var dxf = await _documents.GetAsync(session.DxfDocumentId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("DXF document was not found.");
            if (step.DocumentType != DrawVerify.Core.Enums.DocumentType.StepModel || step.GeometryStatus != DrawVerify.Core.Enums.GeometryStatus.Processed) throw new InvalidOperationException("Matching requires a processed STEP model.");
            if (dxf.DocumentType != DrawVerify.Core.Enums.DocumentType.DxfDrawing || dxf.VerificationStatus != DrawVerify.Core.Enums.VerificationStatus.DimensionsExtracted) throw new InvalidOperationException("Matching requires extracted DXF dimensions.");
            var stepVersion = _documents.GetCurrentVersion(step.DocumentId) ?? step.Version ?? throw new InvalidOperationException("STEP version was not found.");
            var dxfVersion = _documents.GetCurrentVersion(dxf.DocumentId) ?? dxf.Version ?? throw new InvalidOperationException("DXF version was not found.");
            stepDocumentVersionId = stepVersion.VersionId;
            dxfDocumentVersionId = dxfVersion.VersionId;
            var current = session with { StepDocumentVersionId = stepVersion.VersionId, DxfDocumentVersionId = dxfVersion.VersionId, Status = VerificationSessionStatus.Pending, ErrorMessage = null };
            var dimensions = await _documents.GetDimensionsAsync(dxf.DocumentId, cancellationToken).ConfigureAwait(false);
            var extracted = _geometry.ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(stepVersion.StoredRelativePath), cancellationToken);
            if (!extracted.Succeeded) throw new InvalidOperationException(extracted.ErrorMessage ?? "Geometry extraction failed.");
            var matches = new DimensionMatchingAlgorithm(_configuration).Match(current, dimensions, extracted.Features, cancellationToken);
            var status = VerificationSessionStatus.DimensionsMatched;
            var completed = current with { Status = status, UpdatedUtc = DateTime.UtcNow };
            var reasonBreakdown = matches.Where(match => match.ReasonCode is not null).GroupBy(match => match.ReasonCode!).ToDictionary(group => group.Key, group => group.Count());
            var missingDxfCount = matches.Count(match => match.Status == DimensionMatchStatus.Unmatched);
            var extraStepCount = matches.Count(match => match.Status == DimensionMatchStatus.MissingDimension);
            var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, action = DocumentAuditAction.ToleranceComparisonApplied.ToString(), toleranceThresholdMm = effectiveToleranceThresholdMm ?? _configuration.ToleranceThresholdMm, angularToleranceDegrees = _configuration.AngularToleranceDegrees, stepDocumentId = step.DocumentId, dxfDocumentId = dxf.DocumentId, stepDocumentVersionId = stepVersion.VersionId, dxfDocumentVersionId = dxfVersion.VersionId, timestampUtc = DateTime.UtcNow, triggeredBy = _authentication.CurrentUser!.UserId, durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds, comparedDimensions = dimensions.Count, matchedDimensions = matches.Count(match => match.Status == DimensionMatchStatus.Matched), mismatchedDimensions = matches.Count(match => match.Status == DimensionMatchStatus.Mismatched), unmatchedDimensions = missingDxfCount, missingDxfCount, unmatchedDimensionsFlagged = missingDxfCount > 0, reasonBreakdown, missingFeatures = extraStepCount, extraStepCount, extraDimensionCount = extraStepCount, rematched = session.Status != VerificationSessionStatus.Pending });
            await _sessions.ReplaceResultsAsync(completed, matches, DocumentAuditAction.ToleranceComparisonApplied, _authentication.CurrentUser!.UserId, details, cancellationToken).ConfigureAwait(false);
            return completed;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        {
            _logger?.Log(exception, "Dimension matching", $"Unable to match verificationSessionId={sessionId}.");
            var failed = session with { Status = VerificationSessionStatus.Error, ErrorMessage = exception.Message, UpdatedUtc = DateTime.UtcNow };
            var details = JsonSerializer.Serialize(new { verificationSessionId = sessionId, stepDocumentId = session.StepDocumentId, dxfDocumentId = session.DxfDocumentId, stepDocumentVersionId, dxfDocumentVersionId, durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds, matches = 0, unmatchedDimensions = 0, missingFeatures = 0, rematched = session.Status != VerificationSessionStatus.Pending, error = SanitizeError(exception.Message) });
            try { await _sessions.ReplaceResultsAsync(failed, Array.Empty<DimensionMatch>(), DocumentAuditAction.DimensionMatchingFailed, _authentication.CurrentUser!.UserId, details, CancellationToken.None).ConfigureAwait(false); } catch (Exception persistenceException) { _logger?.Log(persistenceException, "Dimension matching failure persistence", $"Unable to persist failed verificationSessionId={sessionId}."); throw; }
            throw;
        }
    }
    public async Task<VerificationSession> UpdateToleranceAndRecompareAsync(Guid sessionId, double toleranceThresholdMm, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ManageToleranceComparison);
        if (!double.IsFinite(toleranceThresholdMm) || toleranceThresholdMm < 0) throw new ArgumentOutOfRangeException(nameof(toleranceThresholdMm));
        return await MatchDimensionsToGeometryAsync(sessionId, cancellationToken, toleranceThresholdMm).ConfigureAwait(false);
    }
    public Task<VerificationSession> GetOrCreateSessionAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.MatchDimensionsToGeometry);
        return _sessions.GetOrCreateAsync(stepDocumentId, dxfDocumentId, cancellationToken);
    }
    public Task<IReadOnlyList<DimensionMatch>> GetResultsAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) { EnsureAuthorized(RolePermission.ViewDimensionMatchResults); return _sessions.GetMatchesAsync(sessionId, status, cancellationToken); }
    public Task<IReadOnlyList<DimensionMatch>> GetMissingResultsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ViewMissingDimensions);
        return _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.Unmatched, cancellationToken);
    }
    public Task<IReadOnlyList<DimensionMatch>> GetExtraResultsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ViewExtraDimensions);
        return _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.ExtraDimension, cancellationToken);
    }
    public Task AddCommentAsync(Guid sessionId, Guid dimensionMatchId, string comment, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.CommentOnMissingDimensions);
        if (string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("A dimension comment is required.", nameof(comment));
        return AddCommentPreservingReviewAsync(sessionId, dimensionMatchId, comment, cancellationToken);
    }
    public Task ResolveMissingDimensionAsync(Guid sessionId, Guid dimensionMatchId, DimensionResolutionStatus status, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ResolveUnmatched);
        if (status == DimensionResolutionStatus.Open) throw new ArgumentException("A resolved dimension must have a final status.", nameof(status));
        return ResolvePreservingReviewAsync(sessionId, dimensionMatchId, status, cancellationToken);
    }
    public Task AddExtraDimensionCommentAsync(Guid sessionId, Guid dimensionMatchId, string comment, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.CommentOnExtraDimensions);
        if (string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("An extra dimension comment is required.", nameof(comment));
        return AddReviewAsync(sessionId, dimensionMatchId, comment.Trim(), null, cancellationToken);
    }
    public Task ResolveExtraDimensionAsync(Guid sessionId, Guid dimensionMatchId, DimensionResolutionStatus status, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ResolveExtraDimensions);
        if (status == DimensionResolutionStatus.Open) throw new ArgumentException("A resolved dimension must have a final status.", nameof(status));
        return AddReviewAsync(sessionId, dimensionMatchId, null, status, cancellationToken);
    }
    public async Task<double> GetEffectiveToleranceThresholdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(RolePermission.ViewDimensionMatchResults);
        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Verification session was not found.");
        return session.ToleranceThresholdMm ?? _configuration.ToleranceThresholdMm;
    }
    private void EnsureAuthorized(RolePermission permission) { if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, permission)) throw new UnauthorizedAccessException("Only authenticated Maker or Checker users may access dimension matching."); }
    private async Task AddCommentPreservingReviewAsync(Guid sessionId, Guid dimensionMatchId, string comment, CancellationToken cancellationToken)
    {
        var current = await GetUnmatchedReviewAsync(sessionId, dimensionMatchId, cancellationToken).ConfigureAwait(false);
        await _sessions.UpdateReviewAsync(sessionId, dimensionMatchId, current with { Comment = comment.Trim() }, _authentication.CurrentUser!.UserId, DocumentAuditAction.CommentAdded, cancellationToken).ConfigureAwait(false);
    }
    private async Task ResolvePreservingReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionResolutionStatus status, CancellationToken cancellationToken)
    {
        var current = await GetUnmatchedReviewAsync(sessionId, dimensionMatchId, cancellationToken).ConfigureAwait(false);
        await _sessions.UpdateReviewAsync(sessionId, dimensionMatchId, current with { Status = status }, _authentication.CurrentUser!.UserId, DocumentAuditAction.StatusChanged, cancellationToken).ConfigureAwait(false);
    }
    private async Task<DimensionReview> GetUnmatchedReviewAsync(Guid sessionId, Guid dimensionMatchId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || dimensionMatchId == Guid.Empty) throw new ArgumentException("Session and dimension match IDs are required.");
        var match = (await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.Unmatched, cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.DimensionMatchId == dimensionMatchId);
        if (match is null) throw new InvalidOperationException("The unmatched dimension was not found in the verification session.");
        return match.Review ?? new DimensionReview { DimensionMatchId = dimensionMatchId };
    }
    private async Task AddReviewAsync(Guid sessionId, Guid dimensionMatchId, string? comment, DimensionResolutionStatus? status, CancellationToken cancellationToken)
    {
        var match = (await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.ExtraDimension, cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.DimensionMatchId == dimensionMatchId);
        if (match is null) throw new InvalidOperationException("The extra dimension was not found in the verification session.");
        var current = match.Review ?? new DimensionReview { DimensionMatchId = dimensionMatchId };
        await _sessions.UpdateReviewAsync(sessionId, dimensionMatchId, current with { Comment = comment ?? current.Comment, Status = status ?? current.Status }, _authentication.CurrentUser!.UserId, comment is null ? DocumentAuditAction.StatusChanged : DocumentAuditAction.CommentAdded, cancellationToken).ConfigureAwait(false);
    }
    private static string SanitizeError(string message) => string.IsNullOrWhiteSpace(message) ? "Matching failed." : message.Length > 512 ? message[..512] : message;
}
