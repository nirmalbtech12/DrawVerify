namespace DrawVerify.Geometry.Models;
public sealed record MatchableGeometryExtractionResult(bool Succeeded, IReadOnlyList<GeometryFeatureData> Features, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static MatchableGeometryExtractionResult Failure(string code, string message) => new(false, Array.Empty<GeometryFeatureData>(), code, message);
}
