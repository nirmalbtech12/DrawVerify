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
}
