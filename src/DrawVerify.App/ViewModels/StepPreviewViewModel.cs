using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DrawVerify.Core.Interfaces;
using DrawVerify.OcctService;

namespace DrawVerify.App.ViewModels;

/// <summary>UI-only STEP preview state; model ownership and feature resolution belong to IStepPreviewService.</summary>
public sealed class StepPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICentralizedLogger? _logger;
    private readonly IStepPreviewService _preview;
    private Model3DGroup? _modelContent;
    private Model3DGroup? _highlightContent;
    public StepPreviewViewModel(ICentralizedLogger? logger = null, IStepPreviewService? preview = null) { _logger = logger; _preview = preview ?? new StepPreviewService(logger: logger); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? ModelPath { get; private set; }
    public string? HighlightedFeatureReference { get; private set; }
    public double? HighlightedLocationX { get; private set; }
    public double? HighlightedLocationY { get; private set; }
    public bool IsModelLoaded => _preview.ModelMesh is not null;
    public bool IsPreviewAvailable => !string.IsNullOrWhiteSpace(HighlightedFeatureReference);
    public bool IsFeatureResolved { get; private set; }
    public bool IsCameraAvailable => IsFinite(CameraPosition) && IsFinite(CameraLookDirection) && CameraLookDirection.Length > 0;
    public Model3DGroup? ModelContent => _modelContent;
    public Model3DGroup? HighlightContent => _highlightContent;
    public string PreviewStatus => IsFeatureResolved ? $"Highlighted STEP feature {HighlightedFeatureReference}." : "STEP preview unavailable: the selected feature is not loaded.";
    public double CameraZoom { get; private set; } = 1;
    public Point3D CameraTarget { get; private set; }
    public Point3D CameraPosition { get; private set; } = new(0, 0, 250);
    public Vector3D CameraLookDirection { get; private set; } = new(0, 0, -250);
    public Vector3D CameraUpDirection { get; } = new(0, 1, 0);
    public string CameraStatus => IsFeatureResolved && IsCameraAvailable ? $"STEP camera fitted to {HighlightedFeatureReference} (zoom {CameraZoom:0.##})." : "3D camera navigation unavailable.";
    public bool LoadModel(string path)
    {
        if (!_preview.LoadModel(path)) { ModelPath = null; _modelContent = null; NotifyAll(); return false; }
        ModelPath = path; _modelContent = BuildMesh(_preview.ModelMesh!); NotifyAll(); return true;
    }
    public bool HighlightAndFit(string? featureReference, double? locationX, double? locationY)
    {
        HighlightedFeatureReference = featureReference; HighlightedLocationX = locationX; HighlightedLocationY = locationY; IsFeatureResolved = false; CameraZoom = 1; _highlightContent = null;
        var highlight = _preview.ResolveHighlight(featureReference);
        if (highlight is not null)
        {
            _highlightContent = highlight.IsEdge ? BuildEdgeHighlight(highlight.EdgePoints, Brushes.Orange) : BuildMesh(highlight.FaceTriangles, _preview.ModelMesh!, Brushes.Orange);
            CameraZoom = 2; UpdateCamera(highlight.Bounds); IsFeatureResolved = _highlightContent.Children.Count > 0 && IsCameraAvailable;
        }
        if (!IsFeatureResolved) _logger?.Log(new InvalidOperationException($"STEP feature '{featureReference ?? "<empty>"}' could not produce a rendered highlight."), "STEP preview navigation", "Unable to highlight and fit the selected extra dimension.");
        NotifyAll(); return IsFeatureResolved;
    }
    private static Model3DGroup BuildMesh(StepPreviewMeshData data, Brush? brush = null) => BuildMesh(data.Triangles, data, brush);
    private static Model3DGroup BuildMesh(IReadOnlyList<StepPreviewTriangle> triangles, StepPreviewMeshData data, Brush? brush = null)
    {
        var group = new Model3DGroup(); var positions = new Point3DCollection(data.Vertices.Select(v => new Point3D(v.Point.X, v.Point.Y, v.Point.Z)));
        var indices = new Int32Collection(triangles.SelectMany(t => new[] { t.First, t.Second, t.Third }));
        if (indices.Count > 0) group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = indices }, new DiffuseMaterial(brush ?? Brushes.LightSteelBlue))); return group;
    }
    private static Model3DGroup BuildEdgeHighlight(IReadOnlyList<StepPreviewPoint> points, Brush brush)
    {
        var group = new Model3DGroup(); if (points.Count < 2) return group; var positions = new Point3DCollection();
        for (var i = 0; i < points.Count; i++) { var point = new Point3D(points[i].X, points[i].Y, points[i].Z); var tangent = new Vector3D(points[Math.Min(i + 1, points.Count - 1)].X - points[Math.Max(i - 1, 0)].X, points[Math.Min(i + 1, points.Count - 1)].Y - points[Math.Max(i - 1, 0)].Y, points[Math.Min(i + 1, points.Count - 1)].Z - points[Math.Max(i - 1, 0)].Z); if (tangent.Length <= 1e-9) tangent = new Vector3D(1, 0, 0); else tangent.Normalize(); var up = Math.Abs(Vector3D.DotProduct(tangent, new Vector3D(0, 0, 1))) > .95 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1); var side = Vector3D.CrossProduct(tangent, up); side.Normalize(); var other = Vector3D.CrossProduct(tangent, side); other.Normalize(); positions.Add(point + side * .08 + other * .08); positions.Add(point - side * .08 + other * .08); positions.Add(point - side * .08 - other * .08); positions.Add(point + side * .08 - other * .08); }
        var indices = new List<int>(); for (var i = 0; i < points.Count - 1; i++) { var c = i * 4; var n = (i + 1) * 4; for (var s = 0; s < 4; s++) { var o = (s + 1) % 4; indices.AddRange(new[] { c + s, n + s, n + o, c + s, n + o, c + o }); } } if (indices.Count > 0) group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = new Int32Collection(indices) }, new DiffuseMaterial(brush))); return group;
    }
    private void UpdateCamera(StepPreviewBounds b) { CameraTarget = new Point3D((b.MinimumX + b.MaximumX) / 2, (b.MinimumY + b.MaximumY) / 2, (b.MinimumZ + b.MaximumZ) / 2); var d = Math.Sqrt(Math.Pow(b.MaximumX - b.MinimumX, 2) + Math.Pow(b.MaximumY - b.MinimumY, 2) + Math.Pow(b.MaximumZ - b.MinimumZ, 2)); var distance = Math.Max(double.IsFinite(d) ? d * 2 / CameraZoom : 0, 1); CameraPosition = CameraTarget + new Vector3D(0, 0, distance); CameraLookDirection = CameraTarget - CameraPosition; }
    private static bool IsFinite(Point3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z); private static bool IsFinite(Vector3D v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    public void Dispose() { _preview.Dispose(); GC.SuppressFinalize(this); }
    private void NotifyAll() { foreach (var n in new[] { nameof(ModelPath), nameof(IsModelLoaded), nameof(HighlightedFeatureReference), nameof(HighlightedLocationX), nameof(HighlightedLocationY), nameof(IsPreviewAvailable), nameof(IsFeatureResolved), nameof(IsCameraAvailable), nameof(ModelContent), nameof(HighlightContent), nameof(PreviewStatus), nameof(CameraZoom), nameof(CameraTarget), nameof(CameraPosition), nameof(CameraLookDirection), nameof(CameraUpDirection), nameof(CameraStatus) }) Notify(n); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
