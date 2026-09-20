using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IInternalSharedLinkService
{
    Task<InternalSharedLinkDetails> GenerateAsync(Guid documentId, Guid versionId, InternalSharedLinkConfiguration configuration, CancellationToken cancellationToken = default);
    Task<InternalSharedLinkDetails> GetDetailsAsync(Guid linkId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InternalSharedLinkDetails>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid linkId, CancellationToken cancellationToken = default);
    Task<InternalSharedLinkDetails> RegenerateAsync(Guid linkId, InternalSharedLinkConfiguration? configuration = null, CancellationToken cancellationToken = default);
    Task<InternalSharedLinkAccessResult> AuthorizeAsync(InternalSharedLinkAccessRequest request, CancellationToken cancellationToken = default);
}
