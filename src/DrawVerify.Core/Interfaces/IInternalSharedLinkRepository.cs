using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IInternalSharedLinkRepository
{
    Task<InternalSharedLink> CreateAsync(InternalSharedLink link, CancellationToken cancellationToken = default);
    Task<InternalSharedLink?> GetByIdAsync(Guid linkId, CancellationToken cancellationToken = default);
    Task<InternalSharedLink?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InternalSharedLink>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid linkId, DateTime revokedUtc, CancellationToken cancellationToken = default);
    Task<InternalSharedLink> RegenerateAsync(Guid oldLinkId, InternalSharedLink replacement, DateTime revokedUtc, CancellationToken cancellationToken = default);
    Task<InternalSharedLinkUsage> GetUsageAsync(Guid linkId, bool includeUsers, CancellationToken cancellationToken = default);
    Task RecordAccessAsync(Guid linkId, string userId, DateTime accessedUtc, CancellationToken cancellationToken = default);
}
