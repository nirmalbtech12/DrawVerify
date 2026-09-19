using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface ISharedDocumentRepository
{
    Task<IReadOnlyList<SharedDocument>> ListAsync(string recipientUserId, SharedDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SharedDocument?> GetAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default);
    async Task<SharedDocument?> GetAccessStateAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default)
        => await GetAsync(recipientUserId, documentId, versionId, cancellationToken).ConfigureAwait(false);
    Task CreateShareAsync(Guid documentId, Guid versionId, string recipientUserId, string sharedByUserId, string sharedByName, DateTime sharedUtc, string? note = null, CancellationToken cancellationToken = default);
    Task RevokeShareAsync(Guid documentId, Guid versionId, string recipientUserId, CancellationToken cancellationToken = default);
}
