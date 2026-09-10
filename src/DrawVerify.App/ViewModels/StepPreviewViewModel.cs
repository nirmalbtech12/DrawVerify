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
    public string CameraStatus => IsFeatureResolved ? $"STEP camera fitted to {HighlightedFeatureReference} (zoom {CameraZoom:0.##})." : "3D camera navigation unavailable.";

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
        if (_model is not null && TryResolveFeature(featureReference, out var featureId, out var bounds))
        {
            var mesh = _model.GenerateMeshData();
            _highlightContent = BuildMesh(mesh, featureId, Brushes.Orange);
            CameraTarget = new Point3D((bounds.MinimumX + bounds.MaximumX) / 2, (bounds.MinimumY + bounds.MaximumY) / 2, (bounds.MinimumZ + bounds.MaximumZ) / 2);
            CameraZoom = 2;
            IsFeatureResolved = true;
        }
        else
            _logger?.Log(new InvalidOperationException($"STEP feature '{featureReference ?? "<empty>"}' could not be resolved in the loaded model."), "STEP preview navigation", "Unable to highlight and fit the selected extra dimension.");
        NotifyAll();
        return IsPreviewAvailable;
    }

    private bool TryResolveFeature(string? reference, out int id, out OcctBoundingBox bounds)
    {
        id = 0; bounds = default!;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out id)) return false;
        try
        {
            bounds = parts[0] switch { "Edge" => _model!.GetEdgeBoundingBox(id), "Face" => _model!.GetFaceBoundingBox(id), _ => default! };
            return !bounds.IsVoid;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

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
    private void NotifyAll() { foreach (var name in new[] { nameof(ModelPath), nameof(IsModelLoaded), nameof(HighlightedFeatureReference), nameof(HighlightedLocationX), nameof(HighlightedLocationY), nameof(IsPreviewAvailable), nameof(IsFeatureResolved), nameof(ModelContent), nameof(HighlightContent), nameof(PreviewStatus), nameof(CameraZoom), nameof(CameraTarget), nameof(CameraStatus) }) Notify(name); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
