namespace DrawVerify.Geometry.Models;

/// <summary>
/// Geometric feature descriptor (faces, edges, datums) DTO.
/// </summary>
public sealed class FeatureData
{
    public string FeatureType { get; init; } = string.Empty;
    public Dictionary<string, double> NominalDimensions { get; init; } = new();
    public string Unit { get; init; } = "mm";
    public double? LowerTolerance { get; init; }
    public double? UpperTolerance { get; init; }
    public string? Uncertainty { get; init; }
}
