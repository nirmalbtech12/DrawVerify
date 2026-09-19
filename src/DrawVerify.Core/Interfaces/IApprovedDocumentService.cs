using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IApprovedDocumentService
{
    Task<IReadOnlyList<SharedDocument>> ListAsync(SharedDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SharedDocumentAccess> OpenAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default);
    Task<bool> ValidateAccessAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
