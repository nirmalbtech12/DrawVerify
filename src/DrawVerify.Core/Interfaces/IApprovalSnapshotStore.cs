using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IApprovalSnapshotStore
{
    Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, Stream stepArtifact, Stream dxfArtifact, CancellationToken cancellationToken = default)
        => SaveAsync(snapshot, cancellationToken);
    Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
