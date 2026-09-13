using System.Text.Json;
using DrawVerify.Application.Workflow;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;

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
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.SubmitForReview)) failures.Add("Only an authenticated Maker can submit a package for review.");
        var state = await _workflow.GetStateAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (state is not null) failures.AddRange(_machine.ValidateState(state.Status).Failures);
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

    public async Task<SubmissionReadiness> ValidateReadinessAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var readiness = await ValidateReadinessAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        var failures = new List<string>(readiness.Failures); ValidateRequest(request, failures);
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
                if (session.StepDocumentVersionId != request.StepDocumentVersionId || session.DxfDocumentVersionId != request.DxfDocumentVersionId) failures.Add("The verification results reference stale document versions.");
            }
        }
        return new(failures.Count == 0, failures);
    }

    private static void ValidateRequest(SubmissionRequest request, ICollection<string> failures)
    {
        if (request.StepDocumentVersionId == Guid.Empty) failures.Add("STEP file version is not available.");
        if (!request.DxfAvailable || request.DxfDocumentVersionId == Guid.Empty) failures.Add("DXF file version is not available.");
        if (!request.ExtractionCompleted || string.IsNullOrWhiteSpace(request.ExtractionEvidenceJson) || request.ExtractionEvidenceJson == "{}") failures.Add("Extraction is not completed.");
        if (!request.VerificationCompleted || request.VerificationSessionId == Guid.Empty || string.IsNullOrWhiteSpace(request.VerificationEvidenceJson) || request.VerificationEvidenceJson == "{}") failures.Add("Verification is not completed.");
        if (!request.RequiredMetadataPresent) failures.Add("Required drawing metadata is missing.");
        if (!string.IsNullOrWhiteSpace(request.ProcessingError)) failures.Add($"Processing failed: {request.ProcessingError}");
        if (!string.IsNullOrWhiteSpace(request.VerificationError)) failures.Add($"Verification failed: {request.VerificationError}");
    }

    public async Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var readiness = await ValidateReadinessAsync(request, cancellationToken).ConfigureAwait(false);
        var user = _authentication.CurrentUser;
        if (!readiness.IsReady)
        {
            await RecordAuditSafelyAsync(request.PackageId, 1, DocumentAuditAction.SubmissionRejected, "Rejected", readiness.Failures, cancellationToken).ConfigureAwait(false);
            return new(false, string.Join(" ", readiness.Failures), Failures: readiness.Failures);
        }
        var state = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false) ?? new WorkflowState { PackageId = request.PackageId };
        var now = _clock().ToUniversalTime();
        var submission = new ReviewSubmission { PackageId = request.PackageId, Revision = state.Revision, MakerUserId = user!.UserId, MakerDisplayName = user.DisplayName, MakerAuthorized = true, SubmittedUtc = now, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), StepDocumentVersionId = request.StepDocumentVersionId, DxfDocumentVersionId = request.DxfDocumentVersionId, VerificationSessionId = request.VerificationSessionId, ExtractionEvidenceJson = request.ExtractionEvidenceJson, VerificationEvidenceJson = request.VerificationEvidenceJson };
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
            if (submission is not null && current?.Version is not null && submission.StepDocumentVersionId != current.Version.VersionId)
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
