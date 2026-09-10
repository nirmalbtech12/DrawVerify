using DrawVerify.Geometry.Models;

namespace DrawVerify.Geometry;

public interface IGdtAnnotationService
{
    GdtAnnotationExtractionResult ExtractAnnotations(string filePath, CancellationToken cancellationToken = default);
}
