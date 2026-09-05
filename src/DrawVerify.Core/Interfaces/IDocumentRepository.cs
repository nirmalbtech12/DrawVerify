using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Persistence contract for documents and their versions.
/// Implemented by Infrastructure; consumed by Application and App.
/// </summary>
public interface IDocumentRepository
{
    Document? Get(Guid documentId);
    DocumentVersion? GetCurrentVersion(Guid documentId);
    void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, GeometryStatus status, string? error);
    GeometryMeasurement? GetGeometry(Guid versionId) => null;
    IReadOnlyList<FeatureMeasurement> GetFeatures(Guid versionId) => Array.Empty<FeatureMeasurement>();
}
