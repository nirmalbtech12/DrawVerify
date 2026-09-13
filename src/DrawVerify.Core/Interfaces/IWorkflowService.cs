using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Orchestrates workflow state transitions. Rules are loaded from configuration,
/// never hard-coded.
/// </summary>
public interface IWorkflowService
{
    Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<SubmissionReadiness> ValidateReadinessAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
        => ValidateReadinessAsync(request.PackageId, cancellationToken);
    Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default);
    Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default);
    Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default);
}
