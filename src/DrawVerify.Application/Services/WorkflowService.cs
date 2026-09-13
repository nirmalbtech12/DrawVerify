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

    public async Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var readiness = await ValidateReadinessAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        var user = _authentication.CurrentUser;
        if (!readiness.IsReady)
        {
            await RecordAuditAsync(request.PackageId, 1, DocumentAuditAction.SubmissionRejected, "Rejected", readiness.Failures, cancellationToken).ConfigureAwait(false);
            return new(false, string.Join(" ", readiness.Failures), Failures: readiness.Failures);
        }
        var state = await _workflow.GetStateAsync(request.PackageId, cancellationToken).ConfigureAwait(false) ?? new WorkflowState { PackageId = request.PackageId };
        var now = _clock().ToUniversalTime();
        var submission = new ReviewSubmission { PackageId = request.PackageId, Revision = state.Revision, MakerUserId = user!.UserId, MakerDisplayName = user.DisplayName, SubmittedUtc = now, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), StepDocumentVersionId = request.StepDocumentVersionId, DxfDocumentVersionId = request.DxfDocumentVersionId, VerificationSessionId = request.VerificationSessionId, ExtractionEvidenceJson = request.ExtractionEvidenceJson, VerificationEvidenceJson = request.VerificationEvidenceJson };
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

    public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) => RecordAuditAsync(packageId, revision, DocumentAuditAction.SubmissionCancelled, "Cancelled", new[] { note?.Trim() ?? string.Empty }, cancellationToken);
    public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => _workflow.GetStateAsync(packageId, cancellationToken);
    public async Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => (await _workflow.GetSubmissionsAsync(Guid.Empty, cancellationToken).ConfigureAwait(false)).Where(x => x.Status == WorkflowStatus.SubmittedForReview).ToArray();
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => _workflow.GetCheckerQueueAsync(cancellationToken);

    private async Task RecordAuditAsync(Guid packageId, int revision, DocumentAuditAction action, string result, IReadOnlyList<string> failures, CancellationToken cancellationToken)
    {
        await _workflow.RecordAuditAsync(CreateAudit(packageId, revision, action, result, _clock().ToUniversalTime(), _authentication.CurrentUser, failures), cancellationToken).ConfigureAwait(false);
    }
    private static AuditEntry CreateAudit(Guid packageId, int revision, DocumentAuditAction action, string result, DateTime timestamp, AuthenticatedUser? user, IReadOnlyList<string>? failures = null) => new() { DocumentId = packageId, UserId = user?.UserId ?? "Anonymous", Action = action, TimestampUtc = timestamp, DetailsJson = JsonSerializer.Serialize(new { packageId, revision, action = action.ToString(), result, makerUserId = user?.UserId, makerDisplayName = user?.DisplayName, failures }) };
}
