namespace DrawVerify.Geometry.Models;

public sealed record GdtAnnotationExtractionResult(bool Succeeded, IReadOnlyList<GdtAnnotationData> Annotations, IReadOnlyList<string> Diagnostics, string? ErrorCategory = null, string? ErrorMessage = null)
{
    public static GdtAnnotationExtractionResult Unsupported(string message) => new(false, Array.Empty<GdtAnnotationData>(), new[] { message }, "CapabilityUnavailable", message);
    public static GdtAnnotationExtractionResult Failure(string category, string message) => new(false, Array.Empty<GdtAnnotationData>(), new[] { message }, category, message);
}
