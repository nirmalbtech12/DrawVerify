using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Persistence contract for documents and their versions.
/// Implemented by Infrastructure; consumed by Application and App.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> FindByOriginalNameAndSizeAsync(string originalFileName, long fileSizeBytes, CancellationToken cancellationToken = default);
    Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default);
    Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default);
}

public interface ITransactionalDocumentRepository
{
    Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default);
}
