using DrawVerify.Core.Models;
<<<<<<< HEAD
=======

>>>>>>> 642060c (KAN-19: implementation)
namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Persistence contract for documents and their versions.
/// Implemented by Infrastructure; consumed by Application and App.
/// </summary>
public interface IDocumentRepository
{
<<<<<<< HEAD
    Document? Get(Guid documentId);
    DocumentVersion? GetCurrentVersion(Guid documentId);
    void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, GeometryStatus status, string? error);
    GeometryMeasurement? GetGeometry(Guid versionId) => null;
    IReadOnlyList<FeatureMeasurement> GetFeatures(Guid versionId) => Array.Empty<FeatureMeasurement>();
=======
    Task<Document?> FindByOriginalNameAndSizeAsync(string originalFileName, long fileSizeBytes, CancellationToken cancellationToken = default);
    Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default);
    Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default);
>>>>>>> 642060c (KAN-19: implementation)
}
