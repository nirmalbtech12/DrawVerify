using System.Text.Json;
using DrawVerify.Application.Fingerprinting;
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
    private WorkflowStateMachine _machine;
    private readonly IWorkflowConfigurationRepository? _configurationRepository;
    private readonly WorkflowConfiguration _startupConfiguration;
    private WorkflowConfiguration _effectiveConfiguration;
    private readonly ICentralizedLogger? _logger;
    private readonly Func<DateTime> _clock;
    private readonly INotificationService? _notifications;
    private readonly IApprovalSnapshotStore? _snapshots;
    private readonly IStepUploadFileStore? _stepFiles;
    private readonly IDxfUploadFileStore? _dxfFiles;
    private readonly IDocumentFingerprintService? _fingerprintService;

    /// <summary>Gets the immutable configuration version most recently resolved for this service operation.</summary>
    public int? EffectiveConfigurationVersion { get; private set; }

    public WorkflowService(IWorkflowRepository workflow, ISessionAuthenticationService authentication, WorkflowConfiguration configuration, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null, IDocumentRepository? documents = null, IVerificationSessionRepository? sessions = null, Func<DateTime>? clock = null, INotificationService? notifications = null, IApprovalSnapshotStore? snapshots = null, IStepUploadFileStore? stepFiles = null, IDxfUploadFileStore? dxfFiles = null, IWorkflowConfigurationRepository? configurationRepository = null, IDocumentFingerprintService? fingerprintService = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow)); _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authorization = authorization ?? new RoleAuthorization(); _startupConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration)); _effectiveConfiguration = _startupConfiguration; _machine = new WorkflowStateMachine(_startupConfiguration); _configurationRepository = configurationRepository; _logger = logger; _documents = documents; _sessions = sessions; _clock = clock ?? (() => DateTime.UtcNow); _notifications = notifications; _snapshots = snapshots; _stepFiles = stepFiles; _dxfFiles = dxfFiles; _fingerprintService = fingerprintService;
    }
    private async Task RefreshEffectiveConfigurationAsync(CancellationToken cancellationToken, int? version = null)
    {
        if (_configurationRepository is null) return;
        var effective = version.HasValue
            ? await _configurationRepository.GetVersionAsync(version.Value, cancellationToken).ConfigureAwait(false)
            : await _configurationRepository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (effective is null) throw new InvalidOperationException("No effective workflow configuration is available.");
        effective.Validate();
        _effectiveConfiguration = effective;
        EffectiveConfigurationVersion = effective.Version;
        _machine = new WorkflowStateMachine(effective);
    }

    private async Task ResolveWorkflowConfigurationAsync(Guid packageId, CancellationToken cancellationToken, bool allowCurrentForNewSubmission = false)
    {
        if (_configurationRepository is null && !IsLegacyConfiguration(_effectiveConfiguration))
            throw new InvalidOperationException("Configured workflow execution requires a persistent configuration repository; no workflow operation was applied.");
        var capturedVersion = await ReadPersistedConfigurationVersionAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (capturedVersion.HasValue)
        {
            await RefreshEffectiveConfigurationAsync(cancellationToken, capturedVersion).ConfigureAwait(false);
            return;
        }

        await RefreshEffectiveConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!allowCurrentForNewSubmission && !IsLegacyConfiguration(_effectiveConfiguration))
            throw new InvalidOperationException($"Workflow package '{packageId}' has no captured configuration version; the active workflow was not evaluated.");
    }

    private static bool IsLegacyConfiguration(WorkflowConfiguration configuration) =>
        configuration.Stages.Count == 0 && configuration.Triggers.Count == 0 && configuration.Notifications.Count == 0 &&
        configuration.StatusTransitions.Count == 0 && configuration.StatusLabels.Count == 0 &&
        configuration.RevisionPolicy == new WorkflowRevisionPolicy();

    private async Task<int?> ReadPersistedConfigurationVersionAsync(Guid packageId, CancellationToken cancellationToken)
    {
        if (_configurationRepository is null) return null;
        var method = _configurationRepository.GetType().GetMethod("GetWorkflowVersionAsync", new[] { typeof(string), typeof(CancellationToken) });
        if (method is null) return null;
        try
        {
            var task = method.Invoke(_configurationRepository, new object[] { packageId.ToString(), cancellationToken }) as Task<int?>;
            return task is null ? null : await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Workflow configuration version resolution", $"Unable to resolve persisted configuration version for packageId={packageId}.");
            throw;
        }
    }

    private async Task CapturePersistedConfigurationVersionAsync(Guid packageId, int? version, CancellationToken cancellationToken)
    {
        if (_configurationRepository is null || !version.HasValue) return;
        var method = _configurationRepository.GetType().GetMethod("CaptureWorkflowVersionAsync", new[] { typeof(string), typeof(int?), typeof(CancellationToken) });
        if (method is null) return;
        try
        {
            var task = method.Invoke(_configurationRepository, new object[] { packageId.ToString(), version, cancellationToken }) as Task;
            if (task is not null) await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Workflow configuration version capture", $"Unable to persist configuration version={version} for packageId={packageId}.");
            throw;
        }
    }

    private async Task NotifyConfiguredAsync(Guid packageId, int revision, ReviewDecision decision, WorkflowNotificationEvent notificationEvent, CancellationToken cancellationToken)
    {
        if (_notifications is null) return;
        var recipients = _machine.NotificationRecipients(notificationEvent);
        var configuredRules = _effectiveConfiguration.Notifications.Where(x => x.Event == notificationEvent).ToArray();
        if (configuredRules.Any(rule => rule.Channels.Any(channel => channel != WorkflowNotificationChannel.InApp)))
            throw new NotSupportedException($"Workflow notification event '{notificationEvent}' specifies a channel that is not available in the configured notification service.");
        if (recipients.Count == 0 && !IsLegacyConfiguration(_effectiveConfiguration))
            throw new InvalidOperationException($"Workflow notification event '{notificationEvent}' has no configured recipients.");
        if (recipients.Count == 0)
        {
            if (notificationEvent is WorkflowNotificationEvent.Approval or WorkflowNotificationEvent.Rejection or WorkflowNotificationEvent.Return)
                recipients = new[] { "Maker", "Checker" };
            else recipients = new[] { "Maker", "Checker" };
        }
        foreach (var recipient in recipients)
        {
            var systemRole = _machineRole(recipient);
            try
            {
                if (string.Equals(systemRole, "Maker", StringComparison.OrdinalIgnoreCase)) await _notifications.NotifyMakerAsync(packageId, revision, decision, cancellationToken).ConfigureAwait(false);
                else if (string.Equals(systemRole, "Checker", StringComparison.OrdinalIgnoreCase)) await _notifications.NotifyCheckerAsync(packageId, revision, decision, cancellationToken).ConfigureAwait(false);
                else throw new InvalidOperationException($"Workflow notification recipient '{recipient}' does not map to a supported canonical workflow role.");
            }
            catch (Exception exception)
            {
                _logger?.Log(exception, "Workflow notification", $"Unable to deliver configured event={notificationEvent} recipient='{recipient}' for packageId={packageId}, revision={revision}.");
                throw new NotificationDeliveryException(packageId, revision, exception);
            }
        }
        string _machineRole(string name) => _effectiveConfiguration.Roles.FirstOrDefault(x => string.Equals(x.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.SystemRole ?? name;
    }

    public async Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        try
        {
        await ResolveWorkflowConfigurationAsync(packageId, cancellationToken, allowCurrentForNewSubmission: true).ConfigureAwait(false);
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
            if (_authentication.IsAuthenticated)
            {
                var currentState = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
                await RecordAuditSafelyAsync(request.PackageId, currentState?.Revision ?? 0, DocumentAuditAction.SubmissionRejected, "Rejected", readiness.Failures, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var currentState = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
                await RecordUnauthenticatedRejectionAuditAsync(request.PackageId, currentState?.Revision ?? 0, readiness.Failures, cancellationToken).ConfigureAwait(false);
            }
            return new(false, string.Join(" ", readiness.Failures), Failures: readiness.Failures);
        }
        var state = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return new(false, "The package has no workflow state and cannot be submitted.", Failures: new[] { "The package has no workflow state and cannot be submitted." });
        var now = _clock().ToUniversalTime();
        DocumentFingerprint? fingerprint = null;
        if (_fingerprintService is not null)
        {
            try
            {
                if (_documents is null || _stepFiles is null || _dxfFiles is null) throw new InvalidOperationException("Fingerprint generation requires the persisted document and artifact stores.");
                var step = await _documents.GetVersionAsync(request.PackageId, request.StepDocumentVersionId, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The submitted STEP version was not found.");
                var dxfDocument = await _documents.GetAsync(request.DxfDocumentId, cancellationToken).ConfigureAwait(false);
                var dxf = dxfDocument?.Versions.FirstOrDefault(x => x.VersionId == request.DxfDocumentVersionId) ?? throw new FileNotFoundException("The submitted DXF version was not found.");
                await using var stepStream = await _stepFiles.OpenReadAsync(step.StoredRelativePath, cancellationToken).ConfigureAwait(false);
                await using var dxfStream = await _dxfFiles.OpenReadAsync(dxf.StoredRelativePath, cancellationToken).ConfigureAwait(false);
                fingerprint = await _fingerprintService.ComputeAsync(new DocumentFingerprintContext(request.PackageId, state.Revision, request.StepDocumentVersionId, request.DxfDocumentVersionId, new Dictionary<string, string> { ["makerUserId"] = user!.UserId, ["extractionEvidence"] = request.ExtractionEvidenceJson, ["verificationEvidence"] = request.VerificationEvidenceJson }), new[] { new DocumentFingerprintArtifact(step.StoredRelativePath, stepStream, step.FileSizeBytes), new DocumentFingerprintArtifact(dxf.StoredRelativePath, dxfStream, dxf.FileSizeBytes) }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.Log(exception, "Fingerprint generation", $"Unable to generate fingerprint for packageId={request.PackageId}, stepVersionId={request.StepDocumentVersionId}, dxfVersionId={request.DxfDocumentVersionId}.");
                var message = $"Fingerprint generation failed: {exception.Message}";
                return new(false, message, Failures: new[] { message });
            }
        }
        try
        {
            var submission = new ReviewSubmission { PackageId = request.PackageId, DxfDocumentId = request.DxfDocumentId, Revision = state.Revision, MakerUserId = user!.UserId, MakerDisplayName = user.DisplayName, MakerAuthorized = true, SubmittedUtc = now, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), StepDocumentVersionId = request.StepDocumentVersionId, DxfDocumentVersionId = request.DxfDocumentVersionId, VerificationSessionId = request.VerificationSessionId, ExtractionEvidenceJson = request.ExtractionEvidenceJson, VerificationEvidenceJson = request.VerificationEvidenceJson, Fingerprint = fingerprint };
            // Establish the durable version reference before the workflow transaction
            // can commit. A capture failure therefore cannot leave a submitted package
            // without the configuration version that governs it.
            await CapturePersistedConfigurationVersionAsync(request.PackageId, EffectiveConfigurationVersion, cancellationToken).ConfigureAwait(false);
            var saved = await _workflow.SubmitAtomicallyAsync(state, submission, CreateAudit(request.PackageId, state.Revision, DocumentAuditAction.SubmissionSucceeded, "Submitted", now, user, documentVersionId: request.StepDocumentVersionId, previousWorkflowStatus: state.Status, workflowStatus: WorkflowStatus.SubmittedForReview, fingerprint: fingerprint), cancellationToken).ConfigureAwait(false);
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
        if (state is not null) await ResolveWorkflowConfigurationAsync(packageId, cancellationToken).ConfigureAwait(false);
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
                await _workflow.MarkSupersededAsync(submission.SubmissionId, CreateAudit(packageId, submission.Revision, DocumentAuditAction.SubmissionSuperseded, "Superseded", _clock().ToUniversalTime(), _authentication.CurrentUser, documentVersionId: submission.StepDocumentVersionId, previousWorkflowStatus: state.Status, workflowStatus: WorkflowStatus.Superseded), cancellationToken).ConfigureAwait(false);
                return state with { Status = WorkflowStatus.Superseded };
            }
        }
        return state;
    }
    public async Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => (await _workflow.GetSubmissionsAsync(Guid.Empty, cancellationToken).ConfigureAwait(false)).Where(x => x.Status == WorkflowStatus.SubmittedForReview).ToArray();
    public async Task<FingerprintVerificationResult> VerifyFingerprintAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
    {
        if (!_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Fingerprint verification is not authorized.");
        if (_fingerprintService is null || _documents is null || _stepFiles is null || _dxfFiles is null) throw new InvalidOperationException("Fingerprint verification is not configured.");
        var submission = (await _workflow.GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Revision == revision) ?? throw new KeyNotFoundException("The submitted version was not found.");
        if (submission.Fingerprint is null) return new FingerprintVerificationResult(FingerprintVerificationStatus.Unavailable, null, "The submitted version has no stored fingerprint.");
        var step = await _documents.GetVersionAsync(packageId, submission.StepDocumentVersionId, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The stored STEP version was not found.");
        var dxfDocument = await _documents.GetAsync(submission.DxfDocumentId, cancellationToken).ConfigureAwait(false);
        var dxf = dxfDocument?.Versions.FirstOrDefault(x => x.VersionId == submission.DxfDocumentVersionId) ?? throw new FileNotFoundException("The stored DXF version was not found.");
        await using var stepStream = await _stepFiles.OpenReadAsync(step.StoredRelativePath, cancellationToken).ConfigureAwait(false);
        await using var dxfStream = await _dxfFiles.OpenReadAsync(dxf.StoredRelativePath, cancellationToken).ConfigureAwait(false);
        return await _fingerprintService.VerifyAsync(submission.Fingerprint, new DocumentFingerprintContext(packageId, revision, submission.StepDocumentVersionId, submission.DxfDocumentVersionId, new Dictionary<string, string> { ["makerUserId"] = submission.MakerUserId, ["extractionEvidence"] = submission.ExtractionEvidenceJson, ["verificationEvidence"] = submission.VerificationEvidenceJson }), new[] { new DocumentFingerprintArtifact(step.StoredRelativePath, stepStream, step.FileSizeBytes), new DocumentFingerprintArtifact(dxf.StoredRelativePath, dxfStream, dxf.FileSizeBytes) }, cancellationToken).ConfigureAwait(false);
    }
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
        => await FinalDecideCoreAsync(packageId, revision, decisionType, null, comments, ReviewDecisionRoute.None, cancellationToken).ConfigureAwait(false);

    public async Task<ReviewDecision> RejectToCheckerAsync(Guid packageId, int revision, string reason, string comments, CancellationToken cancellationToken = default)
    {
        RequireFinalApprovalPermission();
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejection reason is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(comments)) throw new ArgumentException("Rejection comments are required.", nameof(comments));
        return await FinalDecideCoreAsync(packageId, revision, ReviewDecisionType.Return, reason.Trim(), comments.Trim(), ReviewDecisionRoute.ToChecker, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReviewDecision> FinalDecideCoreAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? reason, string? comments, ReviewDecisionRoute route, CancellationToken cancellationToken)
    {
        RequireFinalApprovalPermission();
        if (decisionType is not (ReviewDecisionType.Approve or ReviewDecisionType.Reject or ReviewDecisionType.Return))
            throw new ArgumentOutOfRangeException(nameof(decisionType), "Only Approve, Reject, or Return are valid final decisions.");
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Review package '{packageId}' revision {revision} was not found.");
        await ResolveWorkflowConfigurationAsync(packageId, cancellationToken).ConfigureAwait(false);
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
            OverallReason = string.IsNullOrWhiteSpace(reason) ? (string.IsNullOrWhiteSpace(comments) ? null : comments.Trim()) : reason.Trim(),
            Route = route,
            DestinationRole = route == ReviewDecisionRoute.ToChecker ? "Checker" : null,
            DestinationQueue = route == ReviewDecisionRoute.ToChecker ? "Checker" : null,
            SnapshotReference = persistedSnapshot.SnapshotId.ToString("D"), SnapshotJson = snapshotPayload
        };
        decision.Validate();
        var resultingStatus = decisionType switch
        {
            ReviewDecisionType.Approve => WorkflowStatus.FinallyApproved,
            ReviewDecisionType.Reject when route == ReviewDecisionRoute.ToChecker => WorkflowStatus.ReturnedForRework,
            ReviewDecisionType.Reject => WorkflowStatus.Rejected,
            ReviewDecisionType.Return => WorkflowStatus.ReturnedForRework,
            _ => throw new ArgumentOutOfRangeException(nameof(decisionType))
        };
        var action = decisionType switch
        {
            ReviewDecisionType.Approve => DocumentAuditAction.FinalApproved,
            ReviewDecisionType.Reject when route == ReviewDecisionRoute.ToChecker => DocumentAuditAction.RejectedToChecker,
            ReviewDecisionType.Reject => DocumentAuditAction.Rejected,
            ReviewDecisionType.Return when route == ReviewDecisionRoute.ToChecker => DocumentAuditAction.RejectedToChecker,
            _ => DocumentAuditAction.ReturnedForRework
        };
        ReviewDecision saved;
        try
        {
            saved = await _workflow.SaveReviewDecisionAsync(package, decision, resultingStatus,
                CreateAudit(packageId, revision, action, decisionType.ToString(), decision.DecidedUtc, user,
                    new[] { decision.Comments ?? string.Empty }, decision.SnapshotReference, decision: decision, documentVersionId: package.StepDocumentVersionId, previousWorkflowStatus: package.Status, workflowStatus: resultingStatus), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await _snapshots.DeleteAsync(persistedSnapshot.SnapshotId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanupException) { _logger?.Log(cleanupException, "Final approval snapshot cleanup", $"Unable to remove uncommitted snapshotId={persistedSnapshot.SnapshotId}, packageId={packageId}, revision={revision}."); }
            throw;
        }
        await NotifyConfiguredAsync(packageId, revision, saved, decisionType == ReviewDecisionType.Approve ? WorkflowNotificationEvent.Approval : route == ReviewDecisionRoute.ToChecker ? WorkflowNotificationEvent.Return : WorkflowNotificationEvent.Rejection, cancellationToken).ConfigureAwait(false);
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
        var returnedFromApprover = package.Status == WorkflowStatus.ReturnedForRework
            && package.PriorDecision?.IsFinalApproval == true
            && package.PriorDecision.Route == ReviewDecisionRoute.ToChecker;
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview or WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval)
            && !returnedFromApprover)
            throw new InvalidOperationException($"The package is currently '{package.Status}' and is not available for review.");
        var user = _authentication.CurrentUser!;
        var now = _clock().ToUniversalTime();
        if (package.Status is WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)
            await _workflow.RecordReviewStartedAsync(package, CreateAudit(packageId, revision, DocumentAuditAction.ReviewStarted, "ReviewStarted", now, user, snapshotReference: package.SnapshotReference, documentVersionId: package.StepDocumentVersionId, previousWorkflowStatus: package.Status, workflowStatus: WorkflowStatus.UnderReview), cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default)
        => await DecideCoreAsync(packageId, revision, decisionType, comments, null, false, cancellationToken).ConfigureAwait(false);

    public async Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, string? correctionAction, CancellationToken cancellationToken = default)
        => await DecideCoreAsync(packageId, revision, decisionType, comments, correctionAction, true, cancellationToken).ConfigureAwait(false);

    private async Task<ReviewDecision> DecideCoreAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, string? correctionAction, bool requireCorrectionAction, CancellationToken cancellationToken)
    {
        RequireReviewPermission();
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Review package '{packageId}' revision {revision} was not found.");
        await ResolveWorkflowConfigurationAsync(packageId, cancellationToken).ConfigureAwait(false);
        var returnedFromApprover = package.Status == WorkflowStatus.ReturnedForRework
            && package.PriorDecision?.IsFinalApproval == true
            && package.PriorDecision.Route == ReviewDecisionRoute.ToChecker;
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview) && !returnedFromApprover)
            throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        if (package.PriorDecision is not null && package.PriorDecision.Revision == revision && !returnedFromApprover)
            throw new InvalidOperationException($"A decision was already recorded for this package revision: {package.PriorDecision.DecisionType} at {package.PriorDecision.DecidedUtc:O}.");
        if (decisionType is not (ReviewDecisionType.Approve or ReviewDecisionType.Reject or ReviewDecisionType.RequestChanges or ReviewDecisionType.Return))
            throw new ArgumentOutOfRangeException(nameof(decisionType), "Only Approve, Reject, Request Changes, or Return to Maker are valid review decisions.");
        var user = _authentication.CurrentUser!;
        var reason = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        var decision = new ReviewDecision { PackageId = packageId, Revision = revision, CheckerUserId = user.UserId, CheckerDisplayName = user.DisplayName, DecidedUtc = _clock().ToUniversalTime(), DecisionType = decisionType, Comments = reason, OverallReason = reason, CorrectionAction = string.IsNullOrWhiteSpace(correctionAction) ? null : correctionAction.Trim(), PriorDecisionId = package.PriorDecision?.DecisionId, SnapshotReference = package.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(package) };
        decision.Validate(requireCorrectionAction);
        var status = decisionType switch { ReviewDecisionType.Approve => _machine.ApprovedState, ReviewDecisionType.Reject => _machine.RejectedState, ReviewDecisionType.RequestChanges => _machine.ChangesRequestedState, ReviewDecisionType.Return when package.Status == WorkflowStatus.ReturnedForRework => WorkflowStatus.ReturnedToMaker, ReviewDecisionType.Return => WorkflowStatus.ReturnedForRework, _ => throw new ArgumentOutOfRangeException(nameof(decisionType)) };
        var action = decisionType switch { ReviewDecisionType.Approve => DocumentAuditAction.Approved, ReviewDecisionType.Reject => DocumentAuditAction.Rejected, ReviewDecisionType.Return => DocumentAuditAction.ReturnedForRework, _ => DocumentAuditAction.ChangesRequested };
        var saved = await _workflow.SaveReviewDecisionAsync(package, decision, status, CreateAudit(packageId, revision, action, decisionType.ToString(), decision.DecidedUtc, user, new[] { decision.Comments ?? string.Empty }, decision.SnapshotReference, commentReferences: package.Comments, workflowStatus: status, previousWorkflowStatus: package.Status, decision: decision, documentVersionId: package.StepDocumentVersionId), cancellationToken).ConfigureAwait(false);
        await NotifyConfiguredAsync(packageId, revision, saved, decisionType switch
        {
            ReviewDecisionType.Approve => WorkflowNotificationEvent.Approval,
            ReviewDecisionType.Reject => WorkflowNotificationEvent.Rejection,
            ReviewDecisionType.Return => WorkflowNotificationEvent.Return,
            _ => WorkflowNotificationEvent.Rejection
        }, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public async Task<ReviewComment> AddReviewCommentAsync(Guid packageId, int revision, string text, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        await RequireCurrentRevisionAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Review comment cannot be blank.", nameof(text));
        var user = _authentication.CurrentUser!; var comment = new ReviewComment { PackageId = packageId, Revision = revision, AuthorUserId = user.UserId, AuthorDisplayName = user.DisplayName, CreatedUtc = _clock().ToUniversalTime(), Text = text.Trim(), IsDraft = true };
        await _workflow.SaveReviewCommentAsync(comment, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDraftSaved, "CommentDraftSaved", comment.CreatedUtc, user, comment: comment), cancellationToken).ConfigureAwait(false);
        return comment;
    }

    public async Task<ReviewComment> SaveReviewCommentDraftAsync(Guid packageId, int revision, string text, string? referenceType = null, string? referenceId = null, string? location = null, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        await RequireCurrentRevisionAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Review comment cannot be blank.", nameof(text));
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The review package was not found.");
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)) throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        var user = _authentication.CurrentUser!;
        var comment = new ReviewComment { PackageId = packageId, Revision = revision, AuthorUserId = user.UserId, AuthorDisplayName = user.DisplayName, CreatedUtc = _clock().ToUniversalTime(), Text = text.Trim(), IsDraft = true, ReferenceType = string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim(), ReferenceId = string.IsNullOrWhiteSpace(referenceId) ? null : referenceId.Trim(), Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim() };
        comment.Validate();
        await _workflow.SaveReviewCommentAsync(comment, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDraftSaved, "CommentDraftSaved", comment.CreatedUtc, user, comment: comment, workflowStatus: package.Status), cancellationToken).ConfigureAwait(false);
        return comment;
    }

    public async Task<ReviewComment> UpdateReviewCommentAsync(ReviewComment comment, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var user = _authentication.CurrentUser!;
        await RequireCurrentRevisionAsync(comment.PackageId, comment.Revision, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(user.UserId, comment.AuthorUserId, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Only the comment author can edit an unsent review comment.");
        var package = await _workflow.GetReviewPackageAsync(comment.PackageId, comment.Revision, cancellationToken).ConfigureAwait(false);
        if (package is null) throw new KeyNotFoundException($"Review package '{comment.PackageId}' revision {comment.Revision} was not found.");
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)) throw new ReviewDecisionConflictException(comment.PackageId, comment.Revision, package.Status, comment.Revision, package.PriorDecision);
        var updated = comment with { IsDraft = true };
        updated.Validate();
        await _workflow.UpdateReviewCommentAsync(updated, CreateAudit(comment.PackageId, comment.Revision, DocumentAuditAction.ReviewCommentEdited, "CommentEdited", _clock().ToUniversalTime(), user, comment: updated, workflowStatus: package?.Status), cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task DiscardReviewCommentAsync(Guid packageId, int revision, Guid commentId, CancellationToken cancellationToken = default)
    {
        RequireReviewPermission();
        var user = _authentication.CurrentUser!;
        await RequireCurrentRevisionAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (package is null) throw new KeyNotFoundException($"Review package '{packageId}' revision {revision} was not found.");
        if (package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview)) throw new ReviewDecisionConflictException(packageId, revision, package.Status, revision, package.PriorDecision);
        await _workflow.DiscardReviewCommentAsync(commentId, CreateAudit(packageId, revision, DocumentAuditAction.ReviewCommentDiscarded, "CommentDiscarded", _clock().ToUniversalTime(), user, commentId: commentId, workflowStatus: package?.Status), cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireCurrentRevisionAsync(Guid packageId, int revision, CancellationToken cancellationToken)
    {
        if (packageId == Guid.Empty || revision < 1) throw new ArgumentException("A valid current review revision is required.");
        var submissions = await _workflow.GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false);
        var current = submissions.OrderByDescending(x => x.Revision).FirstOrDefault();
        if (current is not null)
        {
            if (current.Revision != revision)
                throw new ReviewDecisionConflictException(packageId, revision, current.Status, current.Revision, null);
            return;
        }

        // Some legacy review callers do not expose submission history. Preserve
        // their current/draft comment contract; persisted repositories enforce
        // the revision and lock invariant at their mutation boundary.
        if (submissions.Count == 0) return;

        var package = await _workflow.GetReviewPackageAsync(packageId, revision, cancellationToken).ConfigureAwait(false);
        if (package is null)
            throw new ReviewDecisionConflictException(packageId, revision, WorkflowStatus.Draft, 0, null);
        if (package.Revision != revision || package.Status is not (WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview))
            throw new ReviewDecisionConflictException(packageId, revision, package.Status, package.Revision, package.PriorDecision);
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

    public async Task<IReadOnlyList<PriorVersionReview>> GetPriorVersionsAsync(Guid packageId, PriorVersionFilter? filter = null, CancellationToken cancellationToken = default)
    {
        RequirePriorVersionPermission();
        if (packageId == Guid.Empty) throw new ArgumentException("Package identifier is required.", nameof(packageId));
        var submissions = await _workflow.GetPriorSubmissionsAsync(packageId, filter, cancellationToken).ConfigureAwait(false);
        var result = new List<PriorVersionReview>(submissions.Count);
        foreach (var submission in submissions)
        {
            var package = await BuildPriorVersionAsync(submission, cancellationToken).ConfigureAwait(false);
            if (package is not null) result.Add(package);
        }
        return result;
    }

    public async Task<PriorVersionReview?> GetPriorVersionAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
    {
        RequirePriorVersionPermission();
        if (packageId == Guid.Empty) throw new ArgumentException("Package identifier is required.", nameof(packageId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        var submission = (await _workflow.GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Revision == revision);
        if (submission is null) return null;
        var result = await BuildPriorVersionAsync(submission, cancellationToken).ConfigureAwait(false);
        if (result is null) return null;
        var user = _authentication.CurrentUser!;
        var audit = CreateAudit(packageId, revision, DocumentAuditAction.PriorVersionViewed, "PriorVersionViewed", _clock().ToUniversalTime(), user, documentVersionId: submission.StepDocumentVersionId);
        try
        {
            await _workflow.RecordAuditAsync(audit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Prior version audit", $"packageId={packageId}; revision={revision}; versionId={submission.StepDocumentVersionId}");
            throw;
        }
        return result;
    }

    public async Task<VersionComparison> CompareVersionsAsync(Guid packageId, int currentRevision, int priorRevision, CancellationToken cancellationToken = default)
    {
        RequirePriorVersionPermission();
        if (currentRevision < 1 || priorRevision < 1) throw new ArgumentOutOfRangeException(nameof(priorRevision));
        var submissions = await _workflow.GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false);
        var current = submissions.FirstOrDefault(x => x.Revision == currentRevision) ?? throw new KeyNotFoundException("The current revision was not found.");
        var prior = submissions.FirstOrDefault(x => x.Revision == priorRevision) ?? throw new KeyNotFoundException("The prior revision was not found.");
        var fileChanges = new List<VersionDifference>();
        AddDifference(fileChanges, "STEP", prior.StepDocumentVersionId, current.StepDocumentVersionId);
        AddDifference(fileChanges, "DXF", prior.DxfDocumentVersionId, current.DxfDocumentVersionId);
        var metadata = new List<VersionDifference>();
        AddDifference(metadata, "Status", prior.Status.ToString(), current.Status.ToString());
        AddDifference(metadata, "Maker", prior.MakerUserId, current.MakerUserId);
        AddDifference(metadata, "Note", prior.Note, current.Note);
        var dimensions = CompareEvidence(prior.VerificationEvidenceJson, current.VerificationEvidenceJson, "Dimension");
        var gdt = CompareEvidence(prior.VerificationEvidenceJson, current.VerificationEvidenceJson, "Gdt");
        return new VersionComparison { PackageId = packageId, PriorRevision = priorRevision, CurrentRevision = currentRevision, FileChanges = fileChanges, MetadataChanges = metadata, DimensionChanges = dimensions, GdtChanges = gdt, EvidenceAvailable = prior.VerificationEvidenceJson != "{}" && current.VerificationEvidenceJson != "{}" };
    }

    private async Task<PriorVersionReview?> BuildPriorVersionAsync(ReviewSubmission submission, CancellationToken cancellationToken)
    {
        var package = await _workflow.GetReviewPackageAsync(submission.PackageId, submission.Revision, cancellationToken).ConfigureAwait(false);
        if (package is null) return null;
        var decisions = await _workflow.GetDecisionHistoryAsync(submission.PackageId, submission.Revision, cancellationToken).ConfigureAwait(false);
        var decision = decisions.LastOrDefault() ?? package.PriorDecision;
        var correction = package.CorrectionAction ?? package.Comments.Select(x => x.CorrectionResponse).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new PriorVersionReview { PackageId = submission.PackageId, SubmissionId = submission.SubmissionId, Revision = submission.Revision, StepDocumentVersionId = submission.StepDocumentVersionId, DxfDocumentVersionId = submission.DxfDocumentVersionId, VerificationSessionId = submission.VerificationSessionId, MakerUserId = submission.MakerUserId, MakerDisplayName = submission.MakerDisplayName, CreatedUtc = submission.CreatedUtc.ToUniversalTime(), SubmittedUtc = submission.SubmittedUtc.ToUniversalTime(), ApprovedUtc = decisions.Where(x => x.DecisionType == ReviewDecisionType.Approve).Select(x => (DateTime?)x.DecidedUtc.ToUniversalTime()).LastOrDefault(), Status = submission.Status, Note = submission.Note, RejectionReason = package.RejectionReason, CorrectionResponse = correction, Decisions = decisions, Comments = package.Comments, Package = package, IsCurrent = submission.Status is WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview };
    }

    private static void AddDifference(ICollection<VersionDifference> target, string category, Guid previous, Guid current)
    {
        if (previous != current) target.Add(new VersionDifference("File", category, "Modified", previous.ToString("D"), current.ToString("D")));
    }

    private static void AddDifference(ICollection<VersionDifference> target, string identifier, string? previous, string? current)
    {
        if (!string.Equals(previous, current, StringComparison.Ordinal)) target.Add(new VersionDifference("Metadata", identifier, "Modified", previous, current));
    }

    private static IReadOnlyList<VersionDifference> CompareEvidence(string previous, string current, string category)
    {
        if (string.Equals(previous, current, StringComparison.Ordinal)) return Array.Empty<VersionDifference>();
        if (!TryGetEvidenceMap(previous, category, out var previousItems) || !TryGetEvidenceMap(current, category, out var currentItems))
            return Array.Empty<VersionDifference>();

        var differences = new List<VersionDifference>();
        foreach (var identifier in previousItems.Keys.Union(currentItems.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var hasPrevious = previousItems.TryGetValue(identifier, out var previousValue);
            var hasCurrent = currentItems.TryGetValue(identifier, out var currentValue);
            if (!hasPrevious)
                differences.Add(new VersionDifference(category, identifier, "Added", null, currentValue));
            else if (!hasCurrent)
                differences.Add(new VersionDifference(category, identifier, "Removed", previousValue, null));
            else if (!string.Equals(previousValue, currentValue, StringComparison.Ordinal))
                differences.Add(new VersionDifference(category, identifier, "Modified", previousValue, currentValue));
        }
        return differences;
    }

    private static bool TryGetEvidenceMap(string json, string category, out IReadOnlyDictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var propertyNames = category.Equals("Dimension", StringComparison.OrdinalIgnoreCase)
                ? new[] { "dimensions", "dimensionMatches", "matches" }
                : new[] { "gdt", "gdtResults", "gdtControls", "geometricTolerances" };
            JsonElement collection = default;
            foreach (var propertyName in propertyNames)
            {
                if (TryGetPropertyIgnoreCase(document.RootElement, propertyName, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                {
                    collection = candidate;
                    break;
                }
            }
            if (collection.ValueKind != JsonValueKind.Array) return false;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in collection.EnumerateArray())
            {
                var identifier = GetEvidenceIdentifier(item);
                if (!string.IsNullOrWhiteSpace(identifier)) result[identifier] = item.GetRawText();
            }
            values = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetEvidenceIdentifier(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        foreach (var propertyName in new[] { "identifier", "id", "dimensionId", "dimensionMatchId", "gdtResultId", "reference", "dxfDimensionReference" })
            if (TryGetPropertyIgnoreCase(item, propertyName, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }

    private void RequireReviewPermission()
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ReviewVerification))
            throw new UnauthorizedAccessException("Only an authorized Checker or Approver can review drawing packages.");
    }

    private void RequirePriorVersionPermission()
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewPriorVersions))
            throw new UnauthorizedAccessException("Only an authorized Checker or Approver can access prior versions.");
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
    private async Task RecordUnauthenticatedRejectionAuditAsync(Guid packageId, int revision, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        var timestamp = _clock().ToUniversalTime();
        var audit = new AuditEntry
        {
            DocumentId = packageId,
            UserId = "system:unauthenticated",
            ActorRole = "System",
            DocumentRevision = revision.ToString(),
            Action = DocumentAuditAction.SubmissionRejected,
            TimestampUtc = timestamp,
            DetailsJson = JsonSerializer.Serialize(new { packageId, revision, action = DocumentAuditAction.SubmissionRejected.ToString(), result = "Rejected", reason = "Only an authenticated Maker can submit a package for review.", failures })
        };
        try { await _workflow.RecordAuditAsync(audit, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "Workflow audit", $"Unable to record unauthenticated submission rejection for packageId={packageId}, revision={revision}."); throw; }
    }
    private static AuditEntry CreateAudit(Guid packageId, int revision, DocumentAuditAction action, string result, DateTime timestamp, AuthenticatedUser? user, IReadOnlyList<string>? failures = null, string? snapshotReference = null, ReviewComment? comment = null, Guid? commentId = null, WorkflowStatus? workflowStatus = null, IReadOnlyList<ReviewComment>? commentReferences = null, ReviewDecision? decision = null, Guid? documentVersionId = null, WorkflowStatus? previousWorkflowStatus = null, DocumentFingerprint? fingerprint = null) {
        var actor = user ?? throw new UnauthorizedAccessException("An authenticated workflow actor is required for auditing."); var role = actor.Roles.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? throw new InvalidOperationException("The authenticated workflow actor has no role.");
        if (workflowStatus is null && action is DocumentAuditAction.SubmissionSucceeded or DocumentAuditAction.Approved or DocumentAuditAction.Rejected or DocumentAuditAction.ReturnedForRework or DocumentAuditAction.FinalApproved) throw new InvalidOperationException("Workflow transition context is required for decision auditing.");
        return new AuditEntry { DocumentId = packageId, UserId = actor.UserId, ActorRole = role, DocumentRevision = revision.ToString(), DocumentVersion = documentVersionId?.ToString("D"), PreviousWorkflowStatus = previousWorkflowStatus?.ToString(), NewWorkflowStatus = workflowStatus?.ToString(), Action = action, TimestampUtc = timestamp, DetailsJson = JsonSerializer.Serialize(new { packageId, revision, action = action.ToString(), result, fingerprint = fingerprint?.Value, fingerprintAlgorithm = fingerprint?.Algorithm, fingerprintCanonicalizationVersion = fingerprint?.CanonicalizationVersion, checkerUserId = actor.UserId, checkerDisplayName = actor.DisplayName, approverUserId = decision?.ApproverUserId, approverDisplayName = decision?.ApproverDisplayName, workflowStatus = workflowStatus?.ToString(), rejectionStatus = decision is null ? null : action is DocumentAuditAction.Rejected or DocumentAuditAction.RejectedToChecker or DocumentAuditAction.RejectedToMaker or DocumentAuditAction.ReturnedForRework or DocumentAuditAction.ChangesRequested ? workflowStatus?.ToString() : null, route = decision?.Route.ToString(), destinationRole = decision?.DestinationRole, destinationQueue = decision?.DestinationQueue, reason = decision?.OverallReason, comments = decision?.Comments, failures, snapshotReference, commentId = comment?.CommentId ?? commentId, referenceType = comment?.ReferenceType, referenceId = comment?.ReferenceId, location = comment?.Location, commentReferences = commentReferences?.Select(x => new { id = x.CommentId, x.ReferenceType, x.ReferenceId, x.Location }).ToArray(), correctionAction = decision?.CorrectionAction }) };
    }
}
