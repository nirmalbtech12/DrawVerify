using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Persistence contract for documents and their versions.
/// Implemented by Infrastructure; consumed by Application and App.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> FindByOriginalNameAndSizeAsync(string originalFileName, long fileSizeBytes, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
    Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(Get(documentId));
    Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Document? Get(Guid documentId) => GetAsync(documentId).GetAwaiter().GetResult();
    DocumentVersion? GetCurrentVersion(Guid documentId) => Get(documentId)?.Version;
    void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, DrawVerify.Core.Enums.GeometryStatus status, string? error) => throw new NotSupportedException("Measurement persistence is not supported by this document repository.");
    Task ReplaceDimensionsAsync(Guid documentId, Guid documentVersionId, IReadOnlyCollection<DimensionData> dimensions, VerificationStatus status, string? verificationError, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("Dimension persistence is not supported by this document repository."));
    Task ReplaceDimensionsAsync(Guid documentId, Guid documentVersionId, IReadOnlyCollection<DimensionData> dimensions, VerificationStatus status, string? verificationError, Func<AuditEntry> auditEntryFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEntryFactory);
        return ReplaceDimensionsAsync(documentId, documentVersionId, dimensions, status, verificationError, auditEntryFactory(), cancellationToken);
    }
    Task<IReadOnlyList<DimensionData>> GetDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DimensionData>>(Array.Empty<DimensionData>());
}

public interface ITransactionalDocumentRepository
{
    Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default);
}
