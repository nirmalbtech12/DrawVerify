using Generated = DrawVerify.OcctWrapper.Generated;

namespace DrawVerify.Occt;

/// <summary>Loads STEP models through the DrawVerify native OCCT façade.</summary>
public sealed class OcctModelLoader : IOcctModelLoader
{
    public OcctModel LoadStep(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new Generated.StepReader();
        using var result = reader.LoadStep(path);
        if (result.code != Generated.ErrorCode.Success)
        {
            var diagnostics = new List<OcctDiagnostic>(result.diagnostics.Count);
            for (var index = 0; index < result.diagnostics.Count; index++)
            {
                using var diagnostic = result.diagnostics[index];
                diagnostics.Add(new OcctDiagnostic(diagnostic.code.ToString(), diagnostic.operation, diagnostic.message));
            }
            throw new OcctOperationException(result.code.ToString(), result.message, diagnostics);
        }

        return new OcctModel(reader.GetModel());
    }
}

/// <summary>Owns the native model proxy. Dispose it deterministically.</summary>
public sealed class OcctModel : IOcctModel
{
    private Generated.Model? _native;

    internal OcctModel(Generated.Model native) => _native = native;

    public bool IsNull => Native.IsNull();
    public OcctModelSummary GetSummary()
    {
        using var summary = Native.GetModelSummary();
        return new OcctModelSummary(summary.vertexCount, summary.edgeCount, summary.faceCount,
            summary.solidCount, summary.surfaceArea, summary.volume, summary.unit, summary.unitScaleToMillimeters);
    }

    public IReadOnlyList<OcctVertex> GetVertices()
    {
        using var values = Native.GetVertices();
        var result = new List<OcctVertex>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(new OcctVertex(value.id, value.point.x, value.point.y, value.point.z));
        }
        return result;
    }

    public IReadOnlyList<OcctEdge> GetEdges()
    {
        using var values = Native.GetEdges();
        var result = new List<OcctEdge>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(CopyEdge(value));
        }
        return result;
    }

    public IReadOnlyList<OcctFace> GetFaces()
    {
        using var values = Native.GetFaces();
        var result = new List<OcctFace>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(CopyFace(value));
        }
        return result;
    }

    public OcctEdge GetEdge(int edgeId)
    {
        using var value = Native.GetEdge(edgeId);
        return CopyEdge(value);
    }

    public OcctFace GetFace(int faceId)
    {
        using var value = Native.GetFace(faceId);
        return CopyFace(value);
    }

    public OcctValidationResult Validate()
    {
        using var result = Native.ValidateModel();
        return new OcctValidationResult(result.code.ToString(), result.message, result.Succeeded());
    }

    public OcctBoundingBox GetBoundingBox()
    {
        using var value = Native.GetBoundingBox();
        return CopyBoundingBox(value);
    }

    public OcctBoundingBox GetEdgeBoundingBox(int edgeId)
    {
        using var value = Native.GetEdgeBoundingBox(edgeId);
        return CopyBoundingBox(value);
    }

    public OcctBoundingBox GetFaceBoundingBox(int faceId)
    {
        using var value = Native.GetFaceBoundingBox(faceId);
        return CopyBoundingBox(value);
    }

    public IReadOnlyList<int> GetFaceEdges(int faceId) => CopyIdentifiers(Native.GetFaceEdges(faceId));

    public IReadOnlyList<int> GetEdgeVertices(int edgeId) => CopyIdentifiers(Native.GetEdgeVertices(edgeId));

    public IReadOnlyList<int> GetSolidFaces(int solidId) => CopyIdentifiers(Native.GetSolidFaces(solidId));

    public double GetEdgeLength(int edgeId) => Native.GetEdgeLength(edgeId);

    public double GetFaceArea(int faceId) => Native.GetFaceArea(faceId);

    public double GetSurfaceArea() => Native.GetModelSurfaceArea();

    public double GetVolume() => Native.GetModelVolume();

    public double Distance(OcctPoint3 first, OcctPoint3 second)
    {
        using var nativeFirst = new Generated.Point3 { x = first.X, y = first.Y, z = first.Z };
        using var nativeSecond = new Generated.Point3 { x = second.X, y = second.Y, z = second.Z };
        return Native.Distance(nativeFirst, nativeSecond);
    }

    public double MeasureVertexToVertex(int firstVertexId, int secondVertexId)
    {
        var vertices = GetVertices();
        var first = vertices.FirstOrDefault(vertex => vertex.Id == firstVertexId)
            ?? throw new ArgumentOutOfRangeException(nameof(firstVertexId), "Unknown wrapper-local vertex ID.");
        var second = vertices.FirstOrDefault(vertex => vertex.Id == secondVertexId)
            ?? throw new ArgumentOutOfRangeException(nameof(secondVertexId), "Unknown wrapper-local vertex ID.");
        return Distance(new OcctPoint3(first.X, first.Y, first.Z), new OcctPoint3(second.X, second.Y, second.Z));
    }

    public OcctMeshSummary GenerateMesh(double linearDeflection = 0.1, double angularDeflection = 0.5, bool relative = false)
    {
        var mesh = GenerateMeshData(linearDeflection, angularDeflection, relative);
        return new OcctMeshSummary(mesh.Vertices.Count, mesh.Triangles.Count);
    }

    public OcctMeshData GenerateMeshData(double linearDeflection = 0.1, double angularDeflection = 0.5, bool relative = false)
    {
        using var options = new Generated.MeshOptions { linearDeflection = linearDeflection, angularDeflection = angularDeflection, relative = relative };
        using var mesh = Native.GenerateMesh(options);
        var vertices = new List<OcctMeshVertex>(mesh.vertices.Count);
        for (var index = 0; index < mesh.vertices.Count; index++)
        {
            using var value = mesh.vertices[index];
            vertices.Add(new OcctMeshVertex(
                new OcctPoint3(value.point.x, value.point.y, value.point.z),
                new OcctVector3(value.normal.x, value.normal.y, value.normal.z), value.faceId));
        }
        var triangles = new List<OcctMeshTriangle>(mesh.triangles.Count);
        for (var index = 0; index < mesh.triangles.Count; index++)
        {
            using var value = mesh.triangles[index];
            triangles.Add(new OcctMeshTriangle(value.first, value.second, value.third, value.faceId));
        }
        return new OcctMeshData(vertices, triangles);
    }

    public OcctProjection GenerateProjection(OcctProjectionView view)
    {
        if (view == OcctProjectionView.Custom)
        {
            throw new ArgumentOutOfRangeException(nameof(view), "Use GenerateCustomProjection for a custom view.");
        }

        using var projection = Native.GenerateProjection((Generated.ProjectionView)view);
        return CopyProjection(projection);
    }

    public OcctProjection GenerateCustomProjection(OcctVector3 direction, OcctVector3 upDirection)
    {
        if (direction.X == 0 && direction.Y == 0 && direction.Z == 0)
        {
            throw new ArgumentException("Projection direction must be non-zero.", nameof(direction));
        }
        if (upDirection.X == 0 && upDirection.Y == 0 && upDirection.Z == 0)
        {
            throw new ArgumentException("Projection up direction must be non-zero.", nameof(upDirection));
        }
        using var nativeDirection = new Generated.Vector3 { x = direction.X, y = direction.Y, z = direction.Z };
        using var nativeUpDirection = new Generated.Vector3 { x = upDirection.X, y = upDirection.Y, z = upDirection.Z };
        using var projection = Native.GenerateProjectionCustom(nativeDirection, nativeUpDirection);
        return CopyProjection(projection);
    }

    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
        GC.SuppressFinalize(this);
    }

    private Generated.Model Native => _native ?? throw new ObjectDisposedException(nameof(OcctModel));

    private static OcctProjection CopyProjection(Generated.ProjectionResult projection)
    {
        return new OcctProjection(
            CopyLines(projection.visibleLines), CopyLines(projection.hiddenLines),
            CopyArcs(projection.visibleArcs), CopyArcs(projection.hiddenArcs),
            CopyCircles(projection.visibleCircles), CopyCircles(projection.hiddenCircles),
            CopyPolylines(projection.visiblePolylines), CopyPolylines(projection.hiddenPolylines),
            projection.approximationTolerance);
    }

    private static OcctBoundingBox CopyBoundingBox(Generated.BoundingBox3 value) =>
        new(value.minimum.x, value.minimum.y, value.minimum.z, value.maximum.x, value.maximum.y, value.maximum.z, value.isVoid);

    private static OcctEdge CopyEdge(Generated.EdgeInfo value) =>
        new(value.id, value.geometryType.ToString(), value.length, value.closed, value.degenerate,
            new OcctPoint3(value.startPoint.x, value.startPoint.y, value.startPoint.z),
            new OcctPoint3(value.endPoint.x, value.endPoint.y, value.endPoint.z),
            new OcctPoint3(value.center.x, value.center.y, value.center.z),
            new OcctVector3(value.direction.x, value.direction.y, value.direction.z),
            new OcctVector3(value.axis.x, value.axis.y, value.axis.z),
            CopyBoundingBox(value.boundingBox), value.radius, value.majorRadius, value.minorRadius,
            value.startParameter, value.endParameter, value.degree, value.rational);

    private static OcctFace CopyFace(Generated.FaceInfo value) =>
        new(value.id, value.surfaceType.ToString(), value.area, value.orientation,
            CopyBoundingBox(value.boundingBox), new OcctPoint3(value.origin.x, value.origin.y, value.origin.z),
            new OcctVector3(value.normal.x, value.normal.y, value.normal.z),
            new OcctVector3(value.xDirection.x, value.xDirection.y, value.xDirection.z),
            new OcctVector3(value.yDirection.x, value.yDirection.y, value.yDirection.z),
            new OcctVector3(value.axis.x, value.axis.y, value.axis.z), value.radius, value.majorRadius, value.minorRadius, value.semiAngle);

    private static IReadOnlyList<int> CopyIdentifiers(Generated.IntVector values)
    {
        using (values)
        {
            var result = new List<int>(values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                result.Add(values[index]);
            }
            return result;
        }
    }

    private static IReadOnlyList<OcctLine2> CopyLines(Generated.Line2Vector values)
    {
        var result = new List<OcctLine2>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(new OcctLine2(value.start.x, value.start.y, value.end.x, value.end.y));
        }
        return result;
    }

    private static IReadOnlyList<OcctArc2> CopyArcs(Generated.Arc2Vector values)
    {
        var result = new List<OcctArc2>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(new OcctArc2(value.center.x, value.center.y, value.radius, value.startParameter, value.endParameter));
        }
        return result;
    }

    private static IReadOnlyList<OcctCircle2> CopyCircles(Generated.Circle2Vector values)
    {
        var result = new List<OcctCircle2>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            result.Add(new OcctCircle2(value.center.x, value.center.y, value.radius));
        }
        return result;
    }

    private static IReadOnlyList<OcctPolyline2> CopyPolylines(Generated.Polyline2Vector values)
    {
        var result = new List<OcctPolyline2>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            using var value = values[index];
            var points = new List<OcctPoint2>(value.points.Count);
            for (var pointIndex = 0; pointIndex < value.points.Count; pointIndex++)
            {
                using var point = value.points[pointIndex];
                points.Add(new OcctPoint2(point.x, point.y));
            }
            result.Add(new OcctPolyline2(points));
        }
        return result;
    }
}

public sealed record OcctModelSummary(int VertexCount, int EdgeCount, int FaceCount, int SolidCount,
    double SurfaceArea, double Volume, string Unit, double UnitScaleToMillimeters);

public sealed class OcctOperationException(string errorCode, string message, IReadOnlyList<OcctDiagnostic>? diagnostics = null) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public IReadOnlyList<OcctDiagnostic> Diagnostics { get; } = diagnostics ?? Array.Empty<OcctDiagnostic>();
}

public sealed record OcctDiagnostic(string ErrorCode, string Operation, string Message);
public sealed record OcctVertex(int Id, double X, double Y, double Z);
public sealed record OcctEdge(
    int Id, string GeometryType, double Length, bool IsClosed, bool IsDegenerate,
    OcctPoint3 StartPoint, OcctPoint3 EndPoint, OcctPoint3 Center,
    OcctVector3 Direction, OcctVector3 Axis, OcctBoundingBox BoundingBox,
    double Radius, double MajorRadius, double MinorRadius,
    double StartParameter, double EndParameter, int Degree, bool IsRational);
public sealed record OcctFace(
    int Id, string SurfaceType, double Area, int Orientation, OcctBoundingBox BoundingBox,
    OcctPoint3 Origin, OcctVector3 Normal, OcctVector3 XDirection, OcctVector3 YDirection,
    OcctVector3 Axis, double Radius, double MajorRadius, double MinorRadius, double SemiAngle);
public sealed record OcctMeshSummary(int VertexCount, int TriangleCount);
public sealed record OcctMeshVertex(OcctPoint3 Point, OcctVector3 Normal, int FaceId);
public sealed record OcctMeshTriangle(int First, int Second, int Third, int FaceId);
public sealed record OcctMeshData(IReadOnlyList<OcctMeshVertex> Vertices, IReadOnlyList<OcctMeshTriangle> Triangles);
public sealed record OcctValidationResult(string ErrorCode, string Message, bool IsValid);
public enum OcctProjectionView { Front, Rear, Top, Bottom, Left, Right, Custom }
public sealed record OcctPoint3(double X, double Y, double Z);
public sealed record OcctPoint2(double X, double Y);
public sealed record OcctVector3(double X, double Y, double Z);
public sealed record OcctBoundingBox(double MinimumX, double MinimumY, double MinimumZ, double MaximumX, double MaximumY, double MaximumZ, bool IsVoid);
public sealed record OcctLine2(double StartX, double StartY, double EndX, double EndY);
public sealed record OcctArc2(double CenterX, double CenterY, double Radius, double StartParameter, double EndParameter);
public sealed record OcctCircle2(double CenterX, double CenterY, double Radius);
public sealed record OcctPolyline2(IReadOnlyList<OcctPoint2> Points);
public sealed record OcctProjection(
    IReadOnlyList<OcctLine2> VisibleLines,
    IReadOnlyList<OcctLine2> HiddenLines,
    IReadOnlyList<OcctArc2> VisibleArcs,
    IReadOnlyList<OcctArc2> HiddenArcs,
    IReadOnlyList<OcctCircle2> VisibleCircles,
    IReadOnlyList<OcctCircle2> HiddenCircles,
    IReadOnlyList<OcctPolyline2> VisiblePolylines,
    IReadOnlyList<OcctPolyline2> HiddenPolylines,
    double ApproximationTolerance);
