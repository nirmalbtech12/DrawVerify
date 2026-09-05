namespace DrawVerify.Core.Models;

public sealed record GeometryMeasurement(
    Guid DocumentVersionId,
    double? Volume,
    double MinimumX, double MinimumY, double MinimumZ,
    double MaximumX, double MaximumY, double MaximumZ,
    int FaceCount, int EdgeCount, int VertexCount,
    string Unit,
    string? Uncertainty = null)
{
    public double Length => MaximumX - MinimumX;
    public double Width => MaximumY - MinimumY;
    public double Height => MaximumZ - MinimumZ;
}
