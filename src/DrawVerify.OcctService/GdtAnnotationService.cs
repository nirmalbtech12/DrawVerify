using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.OcctService;

/// <summary>Managed STEP PMI boundary. Native PMI/XCAF support is reported explicitly when unavailable.</summary>
public sealed class GdtAnnotationService : IGdtAnnotationService
{
    public GdtAnnotationExtractionResult ExtractAnnotations(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A STEP file path is required.", nameof(filePath));
        cancellationToken.ThrowIfCancellationRequested();
        return GdtAnnotationExtractionResult.Unsupported("STEP PMI/XCAF annotation extraction is not available in the current native capability.");
    }
}
