using DrawVerify.Geometry.Models;
using DrawVerify.Occt;
namespace DrawVerify.OcctService.Mapping;

/// <summary>
/// Maps OCCT/SWIG types onto the geometry boundary DTOs. This is the only place
/// OCCT types are converted; nothing OCCT crosses this boundary outward.
/// </summary>
public static class OcctToGeometryMapper
{
    public static GeometryData ToGeometryData(OcctModelSummary summary, OcctBoundingBox box)
    {
        var validBox = !box.IsVoid && double.IsFinite(box.MinimumX) && double.IsFinite(box.MaximumX) &&
            double.IsFinite(box.MinimumY) && double.IsFinite(box.MaximumY) && double.IsFinite(box.MinimumZ) && double.IsFinite(box.MaximumZ);
        return new GeometryData { Volume = summary.SolidCount > 0 && double.IsFinite(summary.Volume) && summary.Volume > 0 ? summary.Volume : null,
            BoundingBox = validBox ? new BoundingBox(box.MinimumX, box.MinimumY, box.MinimumZ, box.MaximumX, box.MaximumY, box.MaximumZ) : null,
            FaceCount = summary.FaceCount, EdgeCount = summary.EdgeCount, VertexCount = summary.VertexCount, Unit = summary.Unit };
    }
}
