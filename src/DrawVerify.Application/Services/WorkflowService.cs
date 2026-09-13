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

    public WorkflowService(IWorkflowRepository workflow, ISessionAuthenticationService authentication, WorkflowConfiguration configuration, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null, IDocumentRepository? documents = null, IVerificationSessionRepository? sessions = null, Func<DateTime>? clock = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow)); _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authorization = authorization ?? new RoleAuthorization(); _machine = new WorkflowStateMachine(configuration ?? throw new ArgumentNullException(nameof(configuration))); _logger = logger; _documents = documents; _sessions = sessions; _clock = clock ?? (() => DateTime.UtcNow);
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
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => _workflow.GetCheckerQueueAsync(cancellationToken);

    private async Task RecordAuditAsync(Guid packageId, int revision, DocumentAuditAction action, string result, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        await _workflow.RecordAuditAsync(CreateAudit(packageId, revision, action, result, _clock().ToUniversalTime(), _authentication.CurrentUser, failures), cancellationToken).ConfigureAwait(false);
    }
    private async Task RecordAuditSafelyAsync(Guid packageId, int revision, DocumentAuditAction action, string result, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        try { await RecordAuditAsync(packageId, revision, action, result, failures, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "Workflow audit", $"Unable to record action={action} for packageId={packageId}, revision={revision}."); throw; }
    }
    private static AuditEntry CreateAudit(Guid packageId, int revision, DocumentAuditAction action, string result, DateTime timestamp, AuthenticatedUser? user, IReadOnlyList<string>? failures = null) => new() { DocumentId = packageId, UserId = user?.UserId ?? "Anonymous", Action = action, TimestampUtc = timestamp, DetailsJson = JsonSerializer.Serialize(new { packageId, revision, action = action.ToString(), result, makerUserId = user?.UserId, makerDisplayName = user?.DisplayName, failures }) };
}
