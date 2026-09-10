namespace DrawVerify.Geometry.Models;
public sealed record GeometryFeatureData
{
    public string Reference { get; init; } = string.Empty;
    public string GeometryType { get; init; } = string.Empty;
    public double? Length { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? Distance { get; init; }
    public double? Radius { get; init; }
    public double? Diameter { get; init; }
    public double? AngleDegrees { get; init; }
    public double? LowerTolerance { get; init; }
    public double? UpperTolerance { get; init; }
    public double? LocationX { get; init; }
    public double? LocationY { get; init; }
    public double? LocationZ { get; init; }
    public string? ViewName { get; init; }
}
