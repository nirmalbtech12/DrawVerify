using DrawVerify.Core.Enums;
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
    Task<IReadOnlyList<WorkflowQueueItem>> GetApproverQueueAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Final approval is not supported by this workflow service.");
    Task<ReviewDecision> FinalDecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Final approval is not supported by this workflow service.");
    Task<ReviewDecision> RejectToCheckerAsync(Guid packageId, int revision, string reason, string comments, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Approver rejection routing is not supported by this workflow service.");
    Task<IReadOnlyList<ReviewDecision>> GetApprovalHistoryAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewDecision>>(Array.Empty<ReviewDecision>());
    bool SupportsAccessRuleConfiguration => false;
    Task ConfigureAccessRulesAsync(Guid packageId, int revision, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Access-rule configuration is unavailable in the MVP workflow.");
    Task<ReviewPackage?> OpenReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<ReviewPackage?>(null);
    Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review decisions are not supported by this workflow service.");
    Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, string? correctionAction, CancellationToken cancellationToken = default)
        => DecideAsync(packageId, revision, decisionType, comments, cancellationToken);
    Task<ReviewComment> AddReviewCommentAsync(Guid packageId, int revision, string text, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review comments are not supported by this workflow service.");
    Task<ReviewComment> SaveReviewCommentDraftAsync(Guid packageId, int revision, string text, string? referenceType = null, string? referenceId = null, string? location = null, CancellationToken cancellationToken = default)
        => AddReviewCommentAsync(packageId, revision, text, cancellationToken);
    Task<ReviewComment> UpdateReviewCommentAsync(ReviewComment comment, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review comment editing is not supported by this workflow service.");
    Task DiscardReviewCommentAsync(Guid packageId, int revision, Guid commentId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review comment discard is not supported by this workflow service.");
    Task<ReviewPackage?> GetReviewPackageForReaderAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<ReviewPackage?>(null);
    Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewComment>>(Array.Empty<ReviewComment>());
    Task<IReadOnlyList<ReviewPackage>> GetMakerReviewPackagesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewPackage>>(Array.Empty<ReviewPackage>());
}
