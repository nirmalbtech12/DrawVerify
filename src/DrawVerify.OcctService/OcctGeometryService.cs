using DrawVerify.Geometry;
using DrawVerify.Occt;

namespace DrawVerify.OcctService;

/// <summary>
/// The ONLY layer that knows OCCT. Implements IGeometryService using the SWIG
/// wrapper (DrawVerify.OcctWrapper) and maps results into Geometry DTOs.
/// </summary>
public sealed class OcctGeometryService : IGeometryService
{
    private readonly IOcctModelLoader _loader;

    /// <summary>Creates the production OCCT-backed geometry service.</summary>
    public OcctGeometryService(IOcctModelLoader? loader = null)
    {
        _loader = loader ?? new OcctModelLoader();
    }

    /// <summary>Runtime capability status, including native linkage diagnostics.</summary>
    public OcctRuntimeStatus CheckRuntime() => new OcctRuntime().CheckReadiness();

    /// <summary>Loads one STEP model and returns an owned, disposable model boundary.</summary>
    public OcctModel LoadStep(string path) => _loader.LoadStep(path);
}
