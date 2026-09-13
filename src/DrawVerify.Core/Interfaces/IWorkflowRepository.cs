using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IWorkflowRepository
{
    Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default);
    async Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(string? reviewerUserId, CancellationToken cancellationToken = default)
        => await GetCheckerQueueAsync(cancellationToken).ConfigureAwait(false);
    Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task<ReviewPackage?> GetReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<ReviewPackage?>(null);
    Task RecordReviewStartedAsync(ReviewPackage package, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewComment>>(Array.Empty<ReviewComment>());
    Task SaveReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review decisions are not supported by this workflow repository.");
}
