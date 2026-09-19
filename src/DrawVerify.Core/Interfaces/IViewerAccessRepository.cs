using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IViewerAccessRepository
{
    Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, AuthenticatedUser actor, CancellationToken cancellationToken = default);
    Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, AuthenticatedUser actor, CancellationToken cancellationToken = default);
}
