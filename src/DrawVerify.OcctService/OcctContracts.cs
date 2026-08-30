using Generated = DrawVerify.OcctWrapper.Generated;

namespace DrawVerify.Occt;

/// <summary>Runtime capability boundary for the dynamically linked OCCT integration.</summary>
public interface IOcctRuntime
{
    OcctRuntimeStatus CheckReadiness();
}

/// <summary>STEP model loading boundary used by application code.</summary>
public interface IOcctModelLoader
{
    OcctModel LoadStep(string path);
}

/// <summary>Disposable managed model boundary; no generated or OCCT type is exposed.</summary>
public interface IOcctModel : IDisposable
{
    bool IsNull { get; }
    OcctModelSummary GetSummary();
    IReadOnlyList<OcctVertex> GetVertices();
    IReadOnlyList<OcctEdge> GetEdges();
    IReadOnlyList<OcctFace> GetFaces();
    OcctProjection GenerateProjection(OcctProjectionView view);
    OcctMeshData GenerateMeshData(double linearDeflection = 0.1, double angularDeflection = 0.5, bool relative = false);
}

public sealed class OcctRuntime : IOcctRuntime
{
    public OcctRuntimeStatus CheckReadiness()
    {
        using var result = Generated.OcctRuntime.CheckReadiness();
        return new OcctRuntimeStatus(result.Succeeded(), result.code.ToString(), result.message,
            Generated.OcctRuntime.GetOcctVersion(), Generated.OcctRuntime.GetWrapperVersion(),
            Generated.OcctRuntime.IsStepSupported(), Generated.OcctRuntime.IsProjectionSupported(), Generated.OcctRuntime.IsMeshingSupported());
    }
}

public sealed record OcctRuntimeStatus(
    bool IsReady,
    string ErrorCode,
    string Message,
    string OcctVersion,
    string WrapperVersion,
    bool IsStepSupported,
    bool IsProjectionSupported,
    bool IsMeshingSupported);
