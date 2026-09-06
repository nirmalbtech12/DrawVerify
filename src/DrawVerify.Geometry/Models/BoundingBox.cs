namespace DrawVerify.Geometry.Models;

/// <summary>
/// Axis-aligned bounding box DTO.
/// </summary>
public sealed class BoundingBox
{
    public double MinimumX { get; init; }
    public double MinimumY { get; init; }
    public double MinimumZ { get; init; }
    public double MaximumX { get; init; }
    public double MaximumY { get; init; }
    public double MaximumZ { get; init; }
    public bool IsVoid { get; init; }
    public double Length => MaximumX - MinimumX;
    public double Width => MaximumY - MinimumY;
    public double Height => MaximumZ - MinimumZ;
    public BoundingBox() { }
    public BoundingBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, bool isVoid = false) =>
        (MinimumX, MinimumY, MinimumZ, MaximumX, MaximumY, MaximumZ, IsVoid) = (minX, minY, minZ, maxX, maxY, maxZ, isVoid);
}
