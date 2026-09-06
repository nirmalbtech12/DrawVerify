namespace DrawVerify.Geometry.Models;
public sealed record MeasurementExtractionResult(
    bool Succeeded, GeometryData? Geometry, IReadOnlyList<FeatureData> Features,
    string? ErrorCategory = null, string? ErrorMessage = null)
{
    public static MeasurementExtractionResult Failure(string category, string message, GeometryData? partial = null) =>
        new(false, partial, Array.Empty<FeatureData>(), category, message);
}
