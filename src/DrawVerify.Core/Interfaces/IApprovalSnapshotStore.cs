using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IApprovalSnapshotStore
{
    Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default);
}
