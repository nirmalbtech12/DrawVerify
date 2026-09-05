namespace DrawVerify.Geometry.Models;

/// <summary>
/// Geometry DTO retrieved from a model file. Owned by the boundary — never OCCT types.
/// </summary>
public sealed class GeometryData
{
    public double? Volume { get; init; }
    public BoundingBox? BoundingBox { get; init; }
    public int FaceCount { get; init; }
    public int EdgeCount { get; init; }
    public int VertexCount { get; init; }
    public string Unit { get; init; } = "mm";
    public string? Uncertainty { get; init; }
}
