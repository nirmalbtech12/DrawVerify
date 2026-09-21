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
    Task<FingerprintVerificationResult?> GetFingerprintVerificationAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<FingerprintVerificationResult?>(null);
    Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task<ReviewPackage?> GetReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<ReviewPackage?>(null);
    Task RecordReviewStartedAsync(ReviewPackage package, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewComment>>(Array.Empty<ReviewComment>());
    Task SaveReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task UpdateReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => SaveReviewCommentAsync(comment, auditEntry, cancellationToken);
    Task DiscardReviewCommentAsync(Guid commentId, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task<IReadOnlyList<DocumentMarkup>> GetDocumentMarkupsAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentMarkup>>(Array.Empty<DocumentMarkup>());
    Task SaveDocumentMarkupAsync(DocumentMarkup markup, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Review decisions are not supported by this workflow repository.");
    Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, InternalApprovalStamp stamp, CancellationToken cancellationToken = default)
        => SaveReviewDecisionAsync(package, decision, resultingStatus, auditEntry, cancellationToken);
    Task<ReviewSubmission> SaveCheckerSignatureAsync(ReviewPackage package, SignatureData signature, DocumentFingerprint fingerprint, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Checker signatures are not supported by this workflow repository.");
    Task<ReviewSubmission> SaveApproverSignatureAsync(ReviewPackage package, SignatureData signature, DocumentFingerprint fingerprint, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Approver signatures are not supported by this workflow repository.");
    Task<InternalApprovalStamp?> GetInternalApprovalStampAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<InternalApprovalStamp?>(null);
    async Task<IReadOnlyList<WorkflowQueueItem>> GetApproverQueueAsync(string? approverUserId, CancellationToken cancellationToken = default)
        => (await GetCheckerQueueAsync(approverUserId, cancellationToken).ConfigureAwait(false)).Where(x => x.Status is WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval).ToArray();
    Task LockRevisionAsync(Guid packageId, int revision, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<IReadOnlyList<ReviewDecision>> GetDecisionHistoryAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReviewDecision>>(Array.Empty<ReviewDecision>());
    async Task<IReadOnlyList<ReviewSubmission>> GetPriorSubmissionsAsync(Guid packageId, PriorVersionFilter? filter = null, CancellationToken cancellationToken = default)
    {
        filter?.Validate();
        var submissions = await GetSubmissionsAsync(packageId, cancellationToken).ConfigureAwait(false);
        return submissions.Where(x => filter?.Status is null || x.Status == filter.Status)
            .Where(x => filter?.FromUtc is null || x.SubmittedUtc >= filter.FromUtc)
            .Where(x => filter?.ToUtc is null || x.SubmittedUtc <= filter.ToUtc)
            .Where(x => string.IsNullOrWhiteSpace(filter?.MakerUserId) || string.Equals(x.MakerUserId, filter.MakerUserId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Revision).ToArray();
    }
}
