namespace DrawVerify.Geometry;

/// <summary>
/// Contract for reading geometry from a step/model file. The HARD boundary
/// between the managed application and OCCT. Implemented only by
/// DrawVerify.OcctService. No OCCT types ever leak through this interface.
/// </summary>
public interface IGeometryService
{
}