using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

public sealed record DxfGdtExtractionResult(
    IReadOnlyList<GdtAnnotationData> Annotations,
    IReadOnlyList<string> Diagnostics,
    bool Succeeded = true,
    string? ErrorCategory = null,
    string? ErrorMessage = null)
{
    public static DxfGdtExtractionResult Success(IReadOnlyList<GdtAnnotationData> annotations, IReadOnlyList<string>? diagnostics = null) => new(annotations, diagnostics ?? Array.Empty<string>());
    public static DxfGdtExtractionResult Failure(string category, string message, IReadOnlyList<string>? diagnostics = null)
        => new(Array.Empty<GdtAnnotationData>(), diagnostics ?? new[] { message }, false, category, message);
}
