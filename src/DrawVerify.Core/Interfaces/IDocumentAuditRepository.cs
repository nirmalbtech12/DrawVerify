using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IDocumentAuditRepository
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default);
}
