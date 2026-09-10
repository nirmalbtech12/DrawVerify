using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DrawVerify.Core.Interfaces;
using DrawVerify.Occt;

namespace DrawVerify.App.ViewModels;

/// <summary>Loads the selected STEP model through the OCCT boundary and exposes real mesh geometry to WPF.</summary>
public sealed class StepPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICentralizedLogger? _logger;
    private readonly IOcctModelLoader _loader;
    private OcctModel? _model;
    private Model3DGroup? _modelContent;
    private Model3DGroup? _highlightContent;
    public StepPreviewViewModel(ICentralizedLogger? logger = null, IOcctModelLoader? loader = null) { _logger = logger; _loader = loader ?? new OcctModelLoader(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? ModelPath { get; private set; }
    public string? HighlightedFeatureReference { get; private set; }
    public double? HighlightedLocationX { get; private set; }
    public double? HighlightedLocationY { get; private set; }
    public bool IsModelLoaded => _model is not null;
    public bool IsPreviewAvailable => !string.IsNullOrWhiteSpace(HighlightedFeatureReference);
    public bool IsFeatureResolved { get; private set; }
    public Model3DGroup? ModelContent => _modelContent;
    public Model3DGroup? HighlightContent => _highlightContent;
    public string PreviewStatus => IsFeatureResolved ? $"Highlighted STEP feature {HighlightedFeatureReference}." : "STEP preview unavailable: the selected feature is not loaded.";
    public double CameraZoom { get; private set; } = 1;
    public Point3D CameraTarget { get; private set; }
    public Point3D CameraPosition { get; private set; } = new(0, 0, 250);
    public Vector3D CameraLookDirection { get; private set; } = new(0, 0, -250);
    public Vector3D CameraUpDirection { get; } = new(0, 1, 0);
    public bool IsCameraAvailable => IsFinite(CameraPosition) && IsFinite(CameraLookDirection) && CameraLookDirection.Length > 0;
    public string CameraStatus => IsFeatureResolved && IsCameraAvailable ? $"STEP camera fitted to {HighlightedFeatureReference} (zoom {CameraZoom:0.##})." : "3D camera navigation unavailable.";

    public bool LoadModel(string path)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            DisposeModel();
            _model = _loader.LoadStep(path);
            if (_model.IsNull) throw new InvalidOperationException("The loaded STEP model is null.");
            ModelPath = path;
            _modelContent = BuildMesh(_model.GenerateMeshData());
            NotifyAll();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or OcctOperationException)
        {
            DisposeModel();
            _logger?.Log(exception, "STEP preview model load", $"Unable to load STEP preview path='{path}'.");
            NotifyAll();
            return false;
        }
    }

    public bool HighlightAndFit(string? featureReference, double? locationX, double? locationY)
    {
        HighlightedFeatureReference = featureReference;
        HighlightedLocationX = locationX;
        HighlightedLocationY = locationY;
        IsFeatureResolved = false;
        CameraZoom = 1;
        _highlightContent = null;
        if (_model is not null && TryResolveFeature(featureReference, out var featureId, out var bounds, out var edge))
        {
            _highlightContent = edge is not null
                ? BuildEdgeHighlight(edge, Brushes.Orange)
                : BuildMesh(_model.GenerateMeshData(), featureId, Brushes.Orange);
            CameraZoom = 2;
            UpdateCamera(bounds);
            IsFeatureResolved = _highlightContent.Children.Count > 0;
            if (!IsFeatureResolved || !IsCameraAvailable)
                _logger?.Log(new InvalidOperationException($"STEP feature '{featureReference}' produced no renderable highlight geometry."), "STEP preview navigation", "Unable to highlight and fit the selected extra dimension.");
        }
        else
            _logger?.Log(new InvalidOperationException($"STEP feature '{featureReference ?? "<empty>"}' could not be resolved in the loaded model."), "STEP preview navigation", "Unable to highlight and fit the selected extra dimension.");
        NotifyAll();
        return IsFeatureResolved && IsCameraAvailable;
    }

    private bool TryResolveFeature(string? reference, out int id, out OcctBoundingBox bounds, out OcctEdge? edge)
    {
        id = 0; bounds = default!; edge = null;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out id)) return false;
        try
        {
            if (parts[0] == "Edge")
            {
                edge = _model!.GetEdge(id);
                bounds = edge.BoundingBox;
            }
            else if (parts[0] == "Face")
                bounds = _model!.GetFaceBoundingBox(id);
            else
                return false;
            return !bounds.IsVoid;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static Model3DGroup BuildEdgeHighlight(OcctEdge edge, Brush brush)
    {
        var group = new Model3DGroup();
        var start = new Point3D(edge.StartPoint.X, edge.StartPoint.Y, edge.StartPoint.Z);
        var end = new Point3D(edge.EndPoint.X, edge.EndPoint.Y, edge.EndPoint.Z);
        var direction = end - start;
        if (edge.IsClosed || direction.Length <= 1e-9 || IsCurved(edge)) return BuildCurvedEdgeHighlight(edge, brush);
        if (!double.IsFinite(direction.Length)) return group;
        direction.Normalize();
        var up = Math.Abs(Vector3D.DotProduct(direction, new Vector3D(0, 0, 1))) > 0.95 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
        var side = Vector3D.CrossProduct(direction, up); side.Normalize();
        var other = Vector3D.CrossProduct(direction, side); other.Normalize();
        var radius = Math.Max(edge.Length * 0.015, 0.05);
        var positions = new Point3DCollection();
        for (var endIndex = 0; endIndex < 2; endIndex++)
        {
            var center = endIndex == 0 ? start : end;
            for (var corner = 0; corner < 4; corner++)
            {
                var offset = (corner is 0 or 3 ? side : -side) * radius + (corner < 2 ? other : -other) * radius;
                positions.Add(center + offset);
            }
        }
        var indices = new Int32Collection(new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 });
        group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = indices }, new DiffuseMaterial(brush)));
        return group;
    }

    private static Model3DGroup BuildCurvedEdgeHighlight(OcctEdge edge, Brush brush)
    {
        var center = new Point3D(edge.Center.X, edge.Center.Y, edge.Center.Z);
        var radius = edge.Radius;
        if (!double.IsFinite(radius) || radius <= 0)
        {
            var box = edge.BoundingBox;
            radius = Math.Max(Math.Max(box.MaximumX - box.MinimumX, box.MaximumY - box.MinimumY), box.MaximumZ - box.MinimumZ) / 2;
            center = new Point3D((box.MinimumX + box.MaximumX) / 2, (box.MinimumY + box.MaximumY) / 2, (box.MinimumZ + box.MaximumZ) / 2);
        }
        if (!IsFinite(center) || !double.IsFinite(radius) || radius <= 0) return new Model3DGroup();
        var normal = new Vector3D(edge.Axis.X, edge.Axis.Y, edge.Axis.Z);
        if (!IsFinite(normal) || normal.Length <= 1e-9) normal = new Vector3D(0, 0, 1);
        normal.Normalize();
        var first = new Vector3D(edge.StartPoint.X - center.X, edge.StartPoint.Y - center.Y, edge.StartPoint.Z - center.Z);
        if (!IsFinite(first) || first.Length <= 1e-9) first = Math.Abs(Vector3D.DotProduct(normal, new Vector3D(0, 0, 1))) > .95 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1);
        first -= normal * Vector3D.DotProduct(first, normal); first.Normalize();
        var second = Vector3D.CrossProduct(normal, first); second.Normalize();
        var closed = edge.IsClosed || (edge.StartPoint.X == edge.EndPoint.X && edge.StartPoint.Y == edge.EndPoint.Y && edge.StartPoint.Z == edge.EndPoint.Z);
        var start = double.IsFinite(edge.StartParameter) ? edge.StartParameter : 0;
        var end = double.IsFinite(edge.EndParameter) && !closed ? edge.EndParameter : start + 2 * Math.PI;
        if (end <= start) end += 2 * Math.PI;
        var count = Math.Max(24, (int)Math.Ceiling(Math.Abs(end - start) * 12));
        var points = Enumerable.Range(0, count + 1).Select(index =>
        {
            var angle = start + (end - start) * index / count;
            return center + first * (Math.Cos(angle) * radius) + second * (Math.Sin(angle) * radius);
        }).ToArray();
        return BuildTube(points, brush);
    }

    private static Model3DGroup BuildTube(IReadOnlyList<Point3D> points, Brush brush)
    {
        var group = new Model3DGroup();
        if (points.Count < 2) return group;
        var positions = new Point3DCollection();
        for (var index = 0; index < points.Count; index++)
        {
            var tangent = points[Math.Min(index + 1, points.Count - 1)] - points[Math.Max(index - 1, 0)];
            if (tangent.Length <= 1e-9) tangent = new Vector3D(1, 0, 0); else tangent.Normalize();
            var up = Math.Abs(Vector3D.DotProduct(tangent, new Vector3D(0, 0, 1))) > .95 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
            var side = Vector3D.CrossProduct(tangent, up); side.Normalize();
            var other = Vector3D.CrossProduct(tangent, side); other.Normalize();
            positions.Add(points[index] + side * .08 + other * .08); positions.Add(points[index] - side * .08 + other * .08);
            positions.Add(points[index] - side * .08 - other * .08); positions.Add(points[index] + side * .08 - other * .08);
        }
        var indices = new List<int>();
        for (var segment = 0; segment < points.Count - 1; segment++)
        {
            var current = segment * 4; var next = (segment + 1) * 4;
            for (var side = 0; side < 4; side++) { var other = (side + 1) % 4; indices.AddRange(new[] { current + side, next + side, next + other, current + side, next + other, current + other }); }
        }
        if (indices.Count > 0) group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = new Int32Collection(indices) }, new DiffuseMaterial(brush)));
        return group;
    }

    private static bool IsCurved(OcctEdge edge) => edge.Radius > 0 || edge.MajorRadius > 0 || edge.MinorRadius > 0 || edge.GeometryType.Contains("Arc", StringComparison.OrdinalIgnoreCase) || edge.GeometryType.Contains("Circle", StringComparison.OrdinalIgnoreCase) || edge.GeometryType.Contains("Curve", StringComparison.OrdinalIgnoreCase);
    private void UpdateCamera(OcctBoundingBox bounds)
    {
        CameraTarget = new Point3D((bounds.MinimumX + bounds.MaximumX) / 2, (bounds.MinimumY + bounds.MaximumY) / 2, (bounds.MinimumZ + bounds.MaximumZ) / 2);
        var diagonal = Math.Sqrt(Math.Pow(bounds.MaximumX - bounds.MinimumX, 2) + Math.Pow(bounds.MaximumY - bounds.MinimumY, 2) + Math.Pow(bounds.MaximumZ - bounds.MinimumZ, 2));
        var distance = Math.Max(double.IsFinite(diagonal) ? diagonal * 2 / CameraZoom : 0, 1);
        CameraPosition = CameraTarget + new Vector3D(0, 0, distance);
        CameraLookDirection = CameraTarget - CameraPosition;
    }
    private static bool IsFinite(Point3D point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    private static bool IsFinite(Vector3D vector) => double.IsFinite(vector.X) && double.IsFinite(vector.Y) && double.IsFinite(vector.Z);

    private static Model3DGroup BuildMesh(OcctMeshData data, int? highlightFaceId = null, Brush? brush = null)
    {
        var group = new Model3DGroup();
        var positions = new Point3DCollection(data.Vertices.Count);
        foreach (var vertex in data.Vertices) positions.Add(new Point3D(vertex.Point.X, vertex.Point.Y, vertex.Point.Z));
        var triangles = data.Triangles.Where(t => highlightFaceId is null || t.FaceId == highlightFaceId).ToArray();
        var indices = new Int32Collection(triangles.SelectMany(t => new[] { t.First, t.Second, t.Third }));
        if (indices.Count > 0) group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = indices }, new DiffuseMaterial(brush ?? Brushes.LightSteelBlue)));
        return group;
    }
    private void DisposeModel() { _model?.Dispose(); _model = null; _modelContent = null; _highlightContent = null; IsFeatureResolved = false; }
    public void Dispose() { DisposeModel(); GC.SuppressFinalize(this); }
    private void NotifyAll() { foreach (var name in new[] { nameof(ModelPath), nameof(IsModelLoaded), nameof(HighlightedFeatureReference), nameof(HighlightedLocationX), nameof(HighlightedLocationY), nameof(IsPreviewAvailable), nameof(IsFeatureResolved), nameof(ModelContent), nameof(HighlightContent), nameof(PreviewStatus), nameof(CameraZoom), nameof(CameraTarget), nameof(CameraPosition), nameof(CameraLookDirection), nameof(CameraUpDirection), nameof(IsCameraAvailable), nameof(CameraStatus) }) Notify(name); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
