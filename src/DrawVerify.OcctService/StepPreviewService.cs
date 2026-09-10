using DrawVerify.Core.Interfaces;
using DrawVerify.Occt;

namespace DrawVerify.OcctService;

/// <summary>UI-safe STEP preview boundary. Native model ownership remains here.</summary>
public interface IStepPreviewService : IDisposable
{
    bool LoadModel(string path);
    StepPreviewMeshData? ModelMesh { get; }
    StepPreviewHighlight? ResolveHighlight(string? reference);
}

public sealed record StepPreviewPoint(double X, double Y, double Z);
public sealed record StepPreviewBounds(double MinimumX, double MinimumY, double MinimumZ, double MaximumX, double MaximumY, double MaximumZ, bool IsVoid);
public sealed record StepPreviewVertex(StepPreviewPoint Point);
public sealed record StepPreviewTriangle(int First, int Second, int Third, int FaceId);
public sealed record StepPreviewMeshData(IReadOnlyList<StepPreviewVertex> Vertices, IReadOnlyList<StepPreviewTriangle> Triangles);
public sealed record StepPreviewHighlight(string Reference, bool IsEdge, IReadOnlyList<StepPreviewPoint> EdgePoints, IReadOnlyList<StepPreviewTriangle> FaceTriangles, StepPreviewBounds Bounds);

public sealed class StepPreviewService : IStepPreviewService
{
    private readonly IOcctModelLoader _loader;
    private readonly ICentralizedLogger? _logger;
    private OcctModel? _model;
    public StepPreviewService(IOcctModelLoader? loader = null, ICentralizedLogger? logger = null) { _loader = loader ?? new OcctModelLoader(); _logger = logger; }
    public StepPreviewMeshData? ModelMesh { get; private set; }
    public bool LoadModel(string path)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            DisposeModel();
            _model = _loader.LoadStep(path);
            if (_model.IsNull) throw new InvalidOperationException("The loaded STEP model is null.");
            var mesh = _model.GenerateMeshData();
            ModelMesh = new StepPreviewMeshData(mesh.Vertices.Select(v => new StepPreviewVertex(new StepPreviewPoint(v.Point.X, v.Point.Y, v.Point.Z))).ToArray(), mesh.Triangles.Select(t => new StepPreviewTriangle(t.First, t.Second, t.Third, t.FaceId)).ToArray());
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or OcctOperationException)
        {
            DisposeModel();
            _logger?.Log(exception, "STEP preview model load", $"Unable to load STEP preview path='{path}'.");
            return false;
        }
    }
    public StepPreviewHighlight? ResolveHighlight(string? reference)
    {
        if (_model is null || string.IsNullOrWhiteSpace(reference)) return null;
        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var id)) return null;
        try
        {
            if (parts[0] == "Edge")
            {
                var edge = _model.GetEdge(id);
                var points = IsCurved(edge) || edge.IsClosed ? SampleCurve(edge) : new[] { Point(edge.StartPoint), Point(edge.EndPoint) };
                return new StepPreviewHighlight(reference, true, points, Array.Empty<StepPreviewTriangle>(), Bounds(edge.BoundingBox));
            }
            if (parts[0] == "Face")
            {
                var bounds = _model.GetFaceBoundingBox(id);
                var triangles = ModelMesh?.Triangles.Where(t => t.FaceId == id).ToArray() ?? Array.Empty<StepPreviewTriangle>();
                return new StepPreviewHighlight(reference, false, Array.Empty<StepPreviewPoint>(), triangles, Bounds(bounds));
            }
        }
        catch (ArgumentOutOfRangeException) { }
        return null;
    }
    private static bool IsCurved(OcctEdge edge) => edge.Radius > 0 || edge.MajorRadius > 0 || edge.MinorRadius > 0 || edge.GeometryType.Contains("Arc", StringComparison.OrdinalIgnoreCase) || edge.GeometryType.Contains("Circle", StringComparison.OrdinalIgnoreCase) || edge.GeometryType.Contains("Curve", StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<StepPreviewPoint> SampleCurve(OcctEdge edge)
    {
        var center = Point(edge.Center); var radius = edge.Radius;
        if (!double.IsFinite(radius) || radius <= 0) radius = Math.Max(Math.Max(edge.BoundingBox.MaximumX - edge.BoundingBox.MinimumX, edge.BoundingBox.MaximumY - edge.BoundingBox.MinimumY), edge.BoundingBox.MaximumZ - edge.BoundingBox.MinimumZ) / 2;
        var normal = new OcctVector3(edge.Axis.X, edge.Axis.Y, edge.Axis.Z); var normalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (!double.IsFinite(normalLength) || normalLength <= 1e-9) normal = new OcctVector3(0, 0, 1); else normal = new OcctVector3(normal.X / normalLength, normal.Y / normalLength, normal.Z / normalLength);
        var first = new OcctVector3(edge.StartPoint.X - center.X, edge.StartPoint.Y - center.Y, edge.StartPoint.Z - center.Z); var firstLength = Math.Sqrt(first.X * first.X + first.Y * first.Y + first.Z * first.Z);
        if (!double.IsFinite(firstLength) || firstLength <= 1e-9) first = Math.Abs(normal.Z) > .95 ? new OcctVector3(1, 0, 0) : new OcctVector3(0, 0, 1); else first = new OcctVector3(first.X / firstLength, first.Y / firstLength, first.Z / firstLength);
        var dot = first.X * normal.X + first.Y * normal.Y + first.Z * normal.Z; first = new OcctVector3(first.X - dot * normal.X, first.Y - dot * normal.Y, first.Z - dot * normal.Z); firstLength = Math.Sqrt(first.X * first.X + first.Y * first.Y + first.Z * first.Z); first = new OcctVector3(first.X / firstLength, first.Y / firstLength, first.Z / firstLength);
        var second = new OcctVector3(normal.Y * first.Z - normal.Z * first.Y, normal.Z * first.X - normal.X * first.Z, normal.X * first.Y - normal.Y * first.X);
        var closed = edge.IsClosed || (edge.StartPoint.X == edge.EndPoint.X && edge.StartPoint.Y == edge.EndPoint.Y && edge.StartPoint.Z == edge.EndPoint.Z); var start = double.IsFinite(edge.StartParameter) ? edge.StartParameter : 0; var end = double.IsFinite(edge.EndParameter) && !closed ? edge.EndParameter : start + 2 * Math.PI; if (end <= start) end += 2 * Math.PI; var count = Math.Max(24, (int)Math.Ceiling(Math.Abs(end - start) * 12));
        return Enumerable.Range(0, count + 1).Select(i => { var angle = start + (end - start) * i / count; return new StepPreviewPoint(center.X + (first.X * Math.Cos(angle) + second.X * Math.Sin(angle)) * radius, center.Y + (first.Y * Math.Cos(angle) + second.Y * Math.Sin(angle)) * radius, center.Z + (first.Z * Math.Cos(angle) + second.Z * Math.Sin(angle)) * radius); }).ToArray();
    }
    private static StepPreviewPoint Point(OcctPoint3 point) => new(point.X, point.Y, point.Z);
    private static StepPreviewBounds Bounds(OcctBoundingBox value) => new(value.MinimumX, value.MinimumY, value.MinimumZ, value.MaximumX, value.MaximumY, value.MaximumZ, value.IsVoid);
    private void DisposeModel() { _model?.Dispose(); _model = null; ModelMesh = null; }
    public void Dispose() { DisposeModel(); GC.SuppressFinalize(this); }
}
