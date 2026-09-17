using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>Document-audit projection backed by the existing append-only audit store.</summary>
public sealed class SqliteDocumentAuditRepository : IDocumentAuditRepository
{
    private readonly SqliteAuditRepository _repository;
    public SqliteDocumentAuditRepository(string databasePath) => _repository = new SqliteAuditRepository(databasePath);
    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => _repository.AppendAsync(entry, cancellationToken);
    public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) => _repository.QueryAsync(query, cancellationToken);
    public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => _repository.VerifyIntegrityAsync(documentId, cancellationToken);
}
