using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

public sealed record DxfGdtExtractionResult(IReadOnlyList<GdtAnnotationData> Annotations, IReadOnlyList<string> Diagnostics)
{
    public static DxfGdtExtractionResult Success(IReadOnlyList<GdtAnnotationData> annotations, IReadOnlyList<string>? diagnostics = null) => new(annotations, diagnostics ?? Array.Empty<string>());
}
