using System.Text.Json;
using DrawVerify.Application.Workflow;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreUploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.Application.Services;

/// <summary>
/// Orchestrates workflow state transitions against configuration-driven rules.
/// </summary>
public sealed class WorkflowService : IWorkflowService
{
    private readonly IWorkflowRepository _workflow;
    private readonly IDocumentRepository? _documents;
    private readonly IVerificationSessionRepository? _sessions;
    private readonly ISessionAuthenticationService _authentication;
    private readonly RoleAuthorization _authorization;
    private readonly WorkflowStateMachine _machine;
    private readonly ICentralizedLogger? _logger;
    private readonly Func<DateTime> _clock;
    private readonly INotificationService? _notifications;
    private readonly IApprovalSnapshotStore? _snapshots;
    private readonly IStepUploadFileStore? _stepFiles;
    private readonly IDxfUploadFileStore? _dxfFiles;

    public WorkflowService(IWorkflowRepository workflow, ISessionAuthenticationService authentication, WorkflowConfiguration configuration, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null, IDocumentRepository? documents = null, IVerificationSessionRepository? sessions = null, Func<DateTime>? clock = null, INotificationService? notifications = null, IApprovalSnapshotStore? snapshots = null, IStepUploadFileStore? stepFiles = null, IDxfUploadFileStore? dxfFiles = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow)); _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authorization = authorization ?? new RoleAuthorization(); _machine = new WorkflowStateMachine(configuration ?? throw new ArgumentNullException(nameof(configuration))); _logger = logger; _documents = documents; _sessions = sessions; _clock = clock ?? (() => DateTime.UtcNow); _notifications = notifications; _snapshots = snapshots; _stepFiles = stepFiles; _dxfFiles = dxfFiles;
    }

    public async Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        try
        {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.SubmitForReview)) failures.Add("Only an authenticated Maker can submit a package for review.");
        var state = await _workflow.GetStateAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (state is null) failures.Add("The package has no workflow state and cannot be submitted.");
        else failures.AddRange(_machine.ValidateState(state.Status).Failures);
        if (_documents is null)
        {
            failures.Add("STEP file is not available.");
            return new(false, failures);
        }
        var step = await _documents.GetAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (step?.Version is null) failures.Add("STEP file is not available.");
        if (step is null) failures.Add("The drawing package was not found.");
        if (step is not null && step.GeometryStatus is CoreGeometryStatus.NotExtracted or CoreGeometryStatus.PendingValidation) failures.Add("Extraction is not completed.");
        if (step is not null && step.GeometryStatus == CoreGeometryStatus.Error) failures.Add("Processing failed during extraction.");
        if (_sessions is null && failures.Count == 0)
        {
            // A session repository is optional for callers that only validate document readiness.
            // The submission request supplies the immutable verification references in that case.
            return new(true, failures);
        }
        return new(failures.Count == 0, failures);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Workflow readiness validation", $"Unable to validate packageId={packageId}; operation=repository-and-consistency-check.");
            throw;
        }
    }

    public async Task<SubmissionReadiness> ValidateReadinessAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
        var readiness = await ValidateReadinessAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        var failures = new List<string>(readiness.Failures);
        await ValidatePersistedPackageAsync(request, failures, cancellationToken).ConfigureAwait(false);
        ValidateRequestPayload(request, failures);
        if (_documents is not null)
        {
            var document = await _documents.GetAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
            if (document?.Version is not null && request.StepDocumentVersionId != Guid.Empty && document.Version.VersionId != request.StepDocumentVersionId) failures.Add("The submitted STEP version is stale; refresh the package before submitting.");
        }
        if (_sessions is not null && request.VerificationSessionId != Guid.Empty)
        {
            var session = await _sessions.GetAsync(request.VerificationSessionId, cancellationToken).ConfigureAwait(false);
            if (session is null) failures.Add("Verification results are not available.");
            else
            {
                if (session.Status == VerificationSessionStatus.Error || !string.IsNullOrWhiteSpace(session.ErrorMessage)) failures.Add("Verification processing failed.");
                if (session.Status != VerificationSessionStatus.DimensionsMatched) failures.Add("Verification is not completed.");
                if (session.StepDocumentId != request.PackageId) failures.Add("The verification results do not belong to the submitted STEP package.");
                if (session.DxfDocumentId != request.DxfDocumentId) failures.Add("The verification results do not belong to the submitted DXF drawing.");
                if (session.StepDocumentVersionId != request.StepDocumentVersionId || session.DxfDocumentVersionId != request.DxfDocumentVersionId) failures.Add("The verification results reference stale document versions.");
            }
        }
        return new(failures.Count == 0, failures);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Workflow readiness validation", $"Unable to validate packageId={request.PackageId}; operation=request-version-and-verification-consistency-check.");
            throw;
        }
    }

    private async Task ValidatePersistedPackageAsync(SubmissionRequest request, ICollection<string> failures, CancellationToken cancellationToken)
    {
        if (_documents is null)
        {
            failures.Add("Persisted drawing package records are not available.");
            return;
        }

        var step = await _documents.GetAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        if (step is null)
            return;

        if (step.DocumentType != CoreDocumentType.StepModel)
            failures.Add("The package record is not a STEP model.");
        if (step.UploadStatus != CoreUploadStatus.Uploaded)
            failures.Add("STEP file is not available for review because its persisted upload did not complete.");
        if (step.Version is null)
            failures.Add("STEP file version is not available in the persisted package.");
        else if (request.StepDocumentVersionId != step.Version.VersionId)
            failures.Add("The submitted STEP version is stale or does not belong to the persisted package.");
        else if (step.Version.DocumentId != Guid.Empty && step.Version.DocumentId != step.DocumentId)
            failures.Add("The persisted STEP version is inconsistent with its document.");
        if (step.GeometryStatus != CoreGeometryStatus.Processed)
            failures.Add(step.GeometryStatus == CoreGeometryStatus.Error ? "Processing failed during extraction." : "Extraction is not completed in the persisted package.");
        if (string.IsNullOrWhiteSpace(step.OriginalFileName) || step.FileSizeBytes <= 0)
            failures.Add("Required STEP drawing metadata is missing.");

        if (request.DxfDocumentId == Guid.Empty)
        {
            failures.Add("DXF drawing record is not available in the persisted package.");
        }
        else
        {
            var dxf = await _documents.GetAsync(request.DxfDocumentId, cancellationToken).ConfigureAwait(false);
            if (dxf is null)
            {
                failures.Add("DXF drawing record is not available in the persisted package.");
            }
            else
            {
                if (dxf.DocumentType != CoreDocumentType.DxfDrawing)
                    failures.Add("The persisted DXF record is not a drawing.");
                if (dxf.UploadStatus != CoreUploadStatus.Uploaded)
                    failures.Add("DXF file is not available for review.");
                if (dxf.Version is null || request.DxfDocumentVersionId != dxf.Version.VersionId)
                    failures.Add("The submitted DXF version is stale or does not belong to the persisted drawing.");
                else if (dxf.Version.DocumentId != Guid.Empty && dxf.Version.DocumentId != dxf.DocumentId)
                    failures.Add("The persisted DXF version is inconsistent with its document.");
                if (dxf.VerificationStatus == VerificationStatus.Error)
                    failures.Add("Processing failed during DXF extraction.");
                else if (dxf.VerificationStatus != VerificationStatus.DimensionsExtracted)
                    failures.Add("Extraction is not completed for the persisted DXF drawing.");
                if (string.IsNullOrWhiteSpace(dxf.OriginalFileName) || dxf.FileSizeBytes <= 0)
                    failures.Add("Required drawing metadata is missing.");
            }
        }
    }

    private static void ValidateRequestPayload(SubmissionRequest request, ICollection<string> failures)
    {
        if (request.StepDocumentVersionId == Guid.Empty) failures.Add("STEP file version is not available.");
        if (request.DxfDocumentVersionId == Guid.Empty) failures.Add("DXF file version is not available.");
        if (string.IsNullOrWhiteSpace(request.ExtractionEvidenceJson) || request.ExtractionEvidenceJson == "{}") failures.Add("Extraction evidence is not available.");
        if (request.VerificationSessionId == Guid.Empty || string.IsNullOrWhiteSpace(request.VerificationEvidenceJson) || request.VerificationEvidenceJson == "{}") failures.Add("Verification results are not available.");
        if (!string.IsNullOrWhiteSpace(request.ProcessingError)) failures.Add($"Processing failed: {request.ProcessingError}");
        if (!string.IsNullOrWhiteSpace(request.VerificationError)) failures.Add($"Verification failed: {request.VerificationError}");
    }

    public async Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var readiness = await ValidateReadinessAsync(request, cancellationToken).ConfigureAwait(false);
        var user = _authentication.CurrentUser;
        if (!readiness.IsReady)
        {
            var currentState = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
            await RecordAuditSafelyAsync(request.PackageId, currentState?.Revision ?? 0, DocumentAuditAction.SubmissionRejected, "Rejected", readiness.Failures, cancellationToken).ConfigureAwait(false);
            return new(false, string.Join(" ", readiness.Failures), Failures: readiness.Failures);
        }
        var state = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return new(false, "The package has no workflow state and cannot be submitted.", Failures: new[] { "The package has no workflow state and cannot be submitted." });
        var now = _clock().ToUniversalTime();
        var submission = new ReviewSubmission { PackageId = request.PackageId, DxfDocumentId = request.DxfDocumentId, Revision = state.Revision, MakerUserId = user!.UserId, MakerDisplayName = user.DisplayName, MakerAuthorized = true, SubmittedUtc = now, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), StepDocumentVersionId = request.StepDocumentVersionId, DxfDocumentVersionId = request.DxfDocumentVersionId, VerificationSessionId = request.VerificationSessionId, ExtractionEvidenceJson = request.ExtractionEvidenceJson, VerificationEvidenceJson = request.VerificationEvidenceJson };
        try
        {
            var saved = await _workflow.SubmitAtomicallyAsync(state, submission, CreateAudit(request.PackageId, state.Revision, DocumentAuditAction.SubmissionSucceeded, "Submitted", now, user), cancellationToken).ConfigureAwait(false);
            return new(true, "The drawing was submitted for review.", state with { Status = WorkflowStatus.SubmittedForReview, UpdatedUtc = now }, saved);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Workflow submission", $"Unable to submit packageId={request.PackageId}.");
            throw;
        }
    }

    public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) => RecordAuditSafelyAsync(packageId, revision, DocumentAuditAction.SubmissionCancelled, "Cancelled", new[] { note?.Trim() ?? string.Empty }, cancellationToken);
    public async Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var state = await _workflow.GetStateAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (state?.Status == WorkflowStatus.SubmittedForReview && _documents is not null)
        {
            var current = await _documents.GetAsync(packageId, cancellationToken).ConfigureAwait(false);
            var submission = (await _workflow.GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false)).Where(x => x.Status == WorkflowStatus.SubmittedForReview).OrderByDescending(x => x.Revision).FirstOrDefault();
            var dxfChanged = submission is not null && submission.DxfDocumentId != Guid.Empty && _documents is not null && (await _documents.GetAsync(submission.DxfDocumentId, cancellationToken).ConfigureAwait(false))?.Version?.VersionId != submission.DxfDocumentVersionId;
            var verificationChanged = false;
            if (submission is not null && _sessions is not null)
            {
                var session = await _sessions.GetAsync(submission.VerificationSessionId, cancellationToken).ConfigureAwait(false);
                verificationChanged = session is null || session.StepDocumentVersionId != submission.StepDocumentVersionId || session.DxfDocumentVersionId != submission.DxfDocumentVersionId;
            }
            if (submission is not null && current?.Version is not null && (submission.StepDocumentVersionId != current.Version.VersionId || dxfChanged || verificationChanged))
            {
                await _workflow.MarkSupersededAsync(submission.SubmissionId, CreateAudit(packageId, submission.Revision, DocumentAuditAction.SubmissionSuperseded, "Superseded", _clock().ToUniversalTime(), _authentication.CurrentUser), cancellationToken).ConfigureAwait(false);
                return state with { Status = WorkflowStatus.Superseded };
            }
        }
        return state;
    }
    public async Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => (await _workflow.GetSubmissionsAsync(Guid.Empty, cancellationToken).ConfigureAwait(false)).Where(x => x.Status == WorkflowStatus.SubmittedForReview).ToArray();
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        return _workflow.GetCheckerQueueAsync(_authentication.CurrentUser!.UserId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowQueueItem>> GetApproverQueueAsync(CancellationToken cancellationToken = default)
    {
        RequireFinalApprovalPermission();
        return _workflow.GetApproverQueueAsync(_authentication.CurrentUser!.UserId, cancellationToken);
    }

    public async Task<ReviewDecision> FinalDecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default)
    {
        RequireFinalApprovalPermission();
        if (decisionType is not (ReviewDecisionType.Approve or ReviewDecisionType.Reject or ReviewDecisionType.Return))
            throw new ArgumentOutOfRangeException(nameof(decisionType), "Only Approve, Reject, or Return are valid final decisions.");
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Review package '{packageId}' revision {revision} was not found.");
        if (package.PriorDecision is not null && package.PriorDecision.IsFinalApproval)
            throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        if (package.Status is not (WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval))
            throw new InvalidOperationException($"The package is currently '{package.Status}' and is not pending final approval.");
        var user = _authentication.CurrentUser!;
        if (_snapshots is null) throw new NotSupportedException("Final approval cannot proceed because immutable snapshot storage is unavailable.");
        if (_documents is null || _stepFiles is null || _dxfFiles is null) throw new NotSupportedException("Final approval cannot proceed because versioned artifact access is unavailable.");
        var stepDocument = await _documents.GetAsync(package.PackageId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The persisted STEP artifact was not found.");
        var dxfDocument = await _documents.GetAsync(package.DxfDocumentId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The persisted DXF artifact was not found.");
        if (stepDocument.Version?.VersionId != package.StepDocumentVersionId || dxfDocument.Version?.VersionId != package.DxfDocumentVersionId) throw new InvalidOperationException("The approval package references stale document versions.");
        var snapshotId = Guid.NewGuid();
        var snapshotPayload = JsonSerializer.Serialize(package);
        var snapshot = new ApprovalSnapshot
        {
            SnapshotId = snapshotId, PackageId = packageId, Revision = revision,
            StepVersionReference = package.StepDocumentVersionId.ToString("D"),
            DxfVersionReference = package.DxfDocumentVersionId.ToString("D"),
            ExtractionEvidenceJson = package.ExtractionEvidenceJson,
            VerificationEvidenceJson = package.VerificationEvidenceJson,
            CheckerDecisionJson = JsonSerializer.Serialize(package.PriorDecision),
            CapturedUtc = _clock().ToUniversalTime()
        };
        ApprovalSnapshot persistedSnapshot;
        await using (var stepArtifact = await _stepFiles.OpenReadAsync(stepDocument.Version.StoredRelativePath, cancellationToken).ConfigureAwait(false))
        await using (var dxfArtifact = await _dxfFiles.OpenReadAsync(dxfDocument.Version.StoredRelativePath, cancellationToken).ConfigureAwait(false))
            persistedSnapshot = await _snapshots.SaveAsync(snapshot, stepArtifact, dxfArtifact, cancellationToken).ConfigureAwait(false);
        var decision = new ReviewDecision
        {
            PackageId = packageId, Revision = revision, CheckerUserId = user.UserId, CheckerDisplayName = user.DisplayName,
            ApproverUserId = user.UserId, ApproverDisplayName = user.DisplayName, IsFinalApproval = true,
            DecidedUtc = _clock().ToUniversalTime(), DecisionType = decisionType,
            Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim(),
            SnapshotReference = persistedSnapshot.SnapshotId.ToString("D"), SnapshotJson = snapshotPayload
        };
        decision.Validate();
        var resultingStatus = decisionType switch
        {
            ReviewDecisionType.Approve => WorkflowStatus.FinallyApproved,
            ReviewDecisionType.Reject => WorkflowStatus.Rejected,
            ReviewDecisionType.Return => WorkflowStatus.ReturnedForRework,
            _ => throw new ArgumentOutOfRangeException(nameof(decisionType))
        };
        var action = decisionType switch
        {
            ReviewDecisionType.Approve => DocumentAuditAction.FinalApproved,
            ReviewDecisionType.Reject => DocumentAuditAction.Rejected,
            _ => DocumentAuditAction.ReturnedForRework
        };
        ReviewDecision saved;
        try
        {
            saved = await _workflow.SaveReviewDecisionAsync(package, decision, resultingStatus,
                CreateAudit(packageId, revision, action, decisionType.ToString(), decision.DecidedUtc, user,
                    new[] { decision.Comments ?? string.Empty }, decision.SnapshotReference), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await _snapshots.DeleteAsync(persistedSnapshot.SnapshotId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanupException) { _logger?.Log(cleanupException, "Final approval snapshot cleanup", $"Unable to remove uncommitted snapshotId={persistedSnapshot.SnapshotId}, packageId={packageId}, revision={revision}."); }
            throw;
        }
        if (_notifications is not null)
        {
            try { await _notifications.NotifyMakerAsync(packageId, revision, saved, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { _logger?.Log(exception, "Final approval notification", $"Unable to notify stakeholders for packageId={packageId}, revision={revision}."); throw new NotificationDeliveryException(packageId, revision, exception); }
            try { await _notifications.NotifyCheckerAsync(packageId, revision, saved, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { _logger?.Log(exception, "Final approval notification", $"Unable to notify Checker for packageId={packageId}, revision={revision}."); throw new NotificationDeliveryException(packageId, revision, exception); }
        }
        return saved;
    }

    public async Task<IReadOnlyList<ReviewDecision>> GetApprovalHistoryAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required to read approval history.");
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (package is null) return Array.Empty<ReviewDecision>();
        var user = _authentication.CurrentUser!;
        var authorized = _authorization.IsAllowed(user, RolePermission.ReviewVerification)
            || _authorization.IsAllowed(user, RolePermission.FinalApproval)
            || string.Equals(user.UserId, package.MakerUserId, StringComparison.OrdinalIgnoreCase);
        if (!authorized) throw new UnauthorizedAccessException("You are not authorized to read approval history for this package.");
        return await _workflow.GetDecisionHistoryAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
    }
    public bool SupportsAccessRuleConfiguration => false;
    public Task ConfigureAccessRulesAsync(Guid packageId, int revision, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Access-rule configuration is unavailable in the MVP workflow; no access change was applied.");

    public async Task<ReviewPackage?> OpenReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (package is null) return null;
        if (_sessions is not null)
        {
            var session = await _sessions.GetAsync(package.VerificationSessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Verification session '{package.VerificationSessionId}' was not found for review package '{packageId}'.");
            package = package with { StepDocumentId = session.StepDocumentId, DxfDocumentId = session.DxfDocumentId };
        }
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview or WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval))
            throw new InvalidOperationException($"The package is currently '{package.Status}' and is not available for review.");
        var user = _authentication.CurrentUser!;
        var now = _clock().ToUniversalTime();
        if (package.Status is WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)
            await _workflow.RecordReviewStartedAsync(package, CreateAudit(packageId, revision, DocumentAuditAction.ReviewStarted, "ReviewStarted", now, user, snapshotReference: package.SnapshotReference), cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default)
        => await DecideAsync(packageId, revision, decisionType, comments, null, cancellationToken).ConfigureAwait(false);

    public async Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, string? correctionAction, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Review package '{packageId}' revision {revision} was not found.");
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview))
            throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        if (package.PriorDecision is not null && package.PriorDecision.Revision == revision)
            throw new InvalidOperationException($"A decision was already recorded for this package revision: {package.PriorDecision.DecisionType} at {package.PriorDecision.DecidedUtc:O}.");
        if (decisionType is not (ReviewDecisionType.Approve or ReviewDecisionType.Reject or ReviewDecisionType.RequestChanges or ReviewDecisionType.Return))
            throw new ArgumentOutOfRangeException(nameof(decisionType), "Only Approve, Reject, Request Changes, or Return to Maker are valid review decisions.");
        var user = _authentication.CurrentUser!;
        var reason = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        var decision = new ReviewDecision { PackageId = packageId, Revision = revision, CheckerUserId = user.UserId, CheckerDisplayName = user.DisplayName, DecidedUtc = _clock().ToUniversalTime(), DecisionType = decisionType, Comments = reason, OverallReason = reason, CorrectionAction = string.IsNullOrWhiteSpace(correctionAction) ? null : correctionAction.Trim(), PriorDecisionId = package.PriorDecision?.DecisionId, SnapshotReference = package.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(package) };
        decision.Validate();
        var status = decisionType switch { ReviewDecisionType.Approve => _machine.ApprovedState, ReviewDecisionType.Reject => _machine.RejectedState, ReviewDecisionType.RequestChanges => _machine.ChangesRequestedState, ReviewDecisionType.Return => WorkflowStatus.ReturnedForRework, _ => throw new ArgumentOutOfRangeException(nameof(decisionType)) };
        var action = decisionType switch { ReviewDecisionType.Approve => DocumentAuditAction.Approved, ReviewDecisionType.Reject => DocumentAuditAction.Rejected, ReviewDecisionType.Return => DocumentAuditAction.ReturnedForRework, _ => DocumentAuditAction.ChangesRequested };
        var saved = await _workflow.SaveReviewDecisionAsync(package, decision, status, CreateAudit(packageId, revision, action, decisionType.ToString(), decision.DecidedUtc, user, new[] { decision.Comments ?? string.Empty }, decision.SnapshotReference, commentReferences: package.Comments, workflowStatus: status, decision: decision), cancellationToken).ConfigureAwait(false);
        if (_notifications is not null)
        {
            try { await _notifications.NotifyMakerAsync(packageId, revision, saved, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { _logger?.Log(exception, "Workflow review notification", $"Unable to notify Maker for packageId={packageId}, revision={revision} after decision persistence succeeded."); throw new NotificationDeliveryException(packageId, revision, exception); }
        }
        return saved;
    }

    public async Task<ReviewComment> AddReviewCommentAsync(Guid packageId, int revision, string text, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Review comment cannot be blank.", nameof(text));
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        var user = _authentication.CurrentUser!; var comment = new ReviewComment { PackageId = packageId, Revision = revision, AuthorUserId = user.UserId, AuthorDisplayName = user.DisplayName, CreatedUtc = _clock().ToUniversalTime(), Text = text.Trim(), IsDraft = true };
        await _workflow.SaveReviewCommentAsync(comment, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDraftSaved, "CommentDraftSaved", comment.CreatedUtc, user, comment: comment, workflowStatus: package?.Status), cancellationToken).ConfigureAwait(false);
        return comment;
    }

    public async Task<ReviewComment> SaveReviewCommentDraftAsync(Guid packageId, int revision, string text, string? referenceType = null, string? referenceId = null, string? location = null, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Review comment cannot be blank.", nameof(text));
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The review package was not found.");
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)) throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        var user = _authentication.CurrentUser!;
        var comment = new ReviewComment { PackageId = packageId, Revision = revision, AuthorUserId = user.UserId, AuthorDisplayName = user.DisplayName, CreatedUtc = _clock().ToUniversalTime(), Text = text.Trim(), IsDraft = true, ReferenceType = referenceType, ReferenceId = referenceId, Location = location };
        await _workflow.SaveReviewCommentAsync(comment, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDraftSaved, "CommentDraftSaved", comment.CreatedUtc, user, comment: comment, workflowStatus: package.Status), cancellationToken).ConfigureAwait(false);
        return comment;
    }

    public async Task<ReviewComment> UpdateReviewCommentAsync(ReviewComment comment, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var user = _authentication.CurrentUser!;
        if (!string.Equals(user.UserId, comment.AuthorUserId, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Only the comment author can edit an unsent review comment.");
        var updated = comment with { IsDraft = true };
        updated.Validate();
        var package = await _workflow.GetReviewPackageAsync(comment.PackageId, comment.Revision, cancellationToken).ConfigureAwait(false);
        await _workflow.UpdateReviewCommentAsync(updated, CreateAudit(comment.PackageId, comment.Revision, DocumentAuditAction.ReviewCommentEdited, "CommentEdited", _clock().ToUniversalTime(), user, comment: updated, workflowStatus: package?.Status), cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task DiscardReviewCommentAsync(Guid packageId, int revision, Guid commentId, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var user = _authentication.CurrentUser!;
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        await _workflow.DiscardReviewCommentAsync(commentId, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDiscarded, "CommentDiscarded", _clock().ToUniversalTime(), user, commentId: commentId, workflowStatus: package?.Status), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewPackage?> GetReviewPackageForReaderAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required to read review comments.");
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (package is null) return null;
        var user = _authentication.CurrentUser!;
        var authorized = _authorization.IsAllowed(user, RolePermission.ReviewVerification) || string.Equals(user.UserId, package.MakerUserId, StringComparison.OrdinalIgnoreCase);
        if (!authorized) throw new UnauthorizedAccessException("You are not authorized to read this review package.");
        return package;
    }

    public async Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => (await GetReviewPackageForReaderAsync(packageId, revision, cancellationToken).ConfigureAwait(false))?.Comments ?? Array.Empty<ReviewComment>();

    public async Task<IReadOnlyList<ReviewPackage>> GetMakerReviewPackagesAsync(CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required to read review comments.");
        var user = _authentication.CurrentUser!;
        var submissions = await _workflow.GetSubmissionsAsync(Guid.Empty, cancellationToken).ConfigureAwait(false);
        var packages = new List<ReviewPackage>();
        foreach (var submission in submissions.Where(x => string.Equals(x.MakerUserId, user.UserId, StringComparison.OrdinalIgnoreCase)))
        {
            var package = await _workflow.GetReviewPackageAsync(submission.PackageId, submission.Revision, cancellationToken).ConfigureAwait(false);
            if (package is not null) packages.Add(package);
        }
        return packages;
    }

    private void RequireReviewPermission()
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ReviewVerification))
            throw new UnauthorizedAccessException("Only an authorized Checker or Approver can review drawing packages.");
    }
    private void RequireFinalApprovalPermission()
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.FinalApproval))
            throw new UnauthorizedAccessException("Only an Approver can access final approval operations.");
    }

    private async Task RecordAuditAsync(Guid packageId, int revision, DocumentAuditAction action, string result, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        await _workflow.RecordAuditAsync(CreateAudit(packageId, revision, action, result, _clock().ToUniversalTime(), _authentication.CurrentUser, failures), cancellationToken).ConfigureAwait(false);
    }
    private async Task RecordAuditSafelyAsync(Guid packageId, int revision, DocumentAuditAction action, string result, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        try { await RecordAuditAsync(packageId, revision, action, result, failures, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "Workflow audit", $"Unable to record action={action} for packageId={packageId}, revision={revision}."); throw; }
    }
    private static AuditEntry CreateAudit(Guid packageId, int revision, DocumentAuditAction action, string result, DateTime timestamp, AuthenticatedUser? user, IReadOnlyList<string>? failures = null, string? snapshotReference = null, ReviewComment? comment = null, Guid? commentId = null, WorkflowStatus? workflowStatus = null, IReadOnlyList<ReviewComment>? commentReferences = null, ReviewDecision? decision = null) => new() { DocumentId = packageId, UserId = user?.UserId ?? "Anonymous", Action = action, TimestampUtc = timestamp, DetailsJson = JsonSerializer.Serialize(new { packageId, revision, action = action.ToString(), result, checkerUserId = user?.UserId, checkerDisplayName = user?.DisplayName, workflowStatus = workflowStatus?.ToString(), rejectionStatus = decision is null ? null : action is DocumentAuditAction.Rejected or DocumentAuditAction.ReturnedForRework or DocumentAuditAction.ChangesRequested ? workflowStatus?.ToString() : null, failures, snapshotReference, commentId = comment?.CommentId ?? commentId, referenceType = comment?.ReferenceType, referenceId = comment?.ReferenceId, location = comment?.Location, commentReferences = commentReferences?.Select(x => new { id = x.CommentId, x.ReferenceType, x.ReferenceId, x.Location }).ToArray(), correctionAction = decision?.CorrectionAction }) };
}
