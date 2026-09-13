using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IWorkflowRepository
{
    Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default);
    Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default);
}
