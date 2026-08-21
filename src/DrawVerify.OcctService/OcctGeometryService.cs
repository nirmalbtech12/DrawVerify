using DrawVerify.Geometry;

namespace DrawVerify.OcctService;

/// <summary>
/// The ONLY layer that knows OCCT. Implements IGeometryService using the SWIG
/// wrapper (DrawVerify.OcctWrapper) and maps results into Geometry DTOs.
/// </summary>
public sealed class OcctGeometryService : IGeometryService
{
}