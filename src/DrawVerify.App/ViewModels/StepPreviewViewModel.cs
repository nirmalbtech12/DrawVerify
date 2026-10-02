using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.OcctService;

namespace DrawVerify.App.ViewModels;

/// <summary>UI-only STEP preview state; model ownership and feature resolution belong to IStepPreviewService.</summary>
public sealed class StepPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICentralizedLogger? _logger;
    private readonly IStepPreviewService _preview;
    private Model3DGroup? _modelContent;
    private Model3DGroup? _highlightContent;
    private Guid _documentId;
    private bool _isProcessing;
    private string _processingStatus = string.Empty;
    private bool _suppressSettingsSave;
    private bool _isSectionEnabled;
    private string _sectionAxis = "Z";
    private double _sectionPosition;
    private readonly SemaphoreSlim _previewLoadGate = new(1, 1);
    private CancellationTokenSource? _activeLoadCancellation;
    public ObservableCollection<StepPreviewModelChoice> Models { get; } = new();
    private StepPreviewModelChoice? _selectedModel;
    public StepPreviewViewModel(ICentralizedLogger? logger = null, IStepPreviewService? preview = null) { _logger = logger; _preview = preview ?? new StepPreviewService(logger: logger); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? ModelPath { get; private set; }
    public bool IsProcessing { get => _isProcessing; private set { _isProcessing = value; Notify(); } }
    public string ProcessingStatus { get => _processingStatus; private set { _processingStatus = value; Notify(); } }
    public StepPreviewModelChoice? SelectedModel { get => _selectedModel; set { if (ReferenceEquals(_selectedModel, value)) return; CancelProcessing(); _selectedModel = value; Notify(); if (value is not null) _ = SelectModelAsync(value); } }
    public void CancelProcessing() => _activeLoadCancellation?.Cancel();
    public void AddUploadedModel(Guid documentId, string name, string path)
    {
        var choice = Models.FirstOrDefault(item => item.DocumentId == documentId && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (choice is null) { choice = new StepPreviewModelChoice(documentId, name, path); Models.Add(choice); }
        SelectedModel = choice;
        Notify(nameof(Models));
    }
    private async Task SelectModelAsync(StepPreviewModelChoice choice)
    {
        var cancellation = new CancellationTokenSource();
        _activeLoadCancellation = cancellation;
        var token = cancellation.Token;
        IsProcessing = true; ProcessingStatus = "Processing 3D model…";
        _modelContent = null; ModelPath = null; NotifyAll();
        try
        {
            if (!choice.IsLoaded)
            {
                var mesh = await LoadMeshAsync(choice.Path, token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                _documentId = choice.DocumentId;
                FitModel(mesh);
                ProcessingStatus = string.Empty;
                RestoreSettings(choice.DocumentId);
                var content = BuildUploadedMesh(mesh);
                if (content.Children.Count == 0) throw new InvalidDataException("The STEP model produced no renderable triangles.");
                choice.IsLoaded = true;
                _modelContent = content; ModelPath = choice.Path;
            }
            else { var mesh = await LoadMeshAsync(choice.Path, token).WaitAsync(token); token.ThrowIfCancellationRequested(); _documentId = choice.DocumentId; FitModel(mesh); ProcessingStatus = string.Empty; RestoreSettings(choice.DocumentId); _modelContent = BuildUploadedMesh(mesh); ModelPath = choice.Path; }
            choice.Error = string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (ReferenceEquals(_activeLoadCancellation, cancellation))
            {
                _modelContent = null; ModelPath = null;
                ProcessingStatus = "Processing cancelled.";
            }
        }
        catch (Exception exception)
        {
            choice.Error = exception.Message;
            _logger?.Log(exception, "Upload STEP preview", $"Unable to load documentId={choice.DocumentId}; path='{choice.Path}'.");
            if (ReferenceEquals(_activeLoadCancellation, cancellation))
            {
                _modelContent = null; ModelPath = null;
                ProcessingStatus = $"STEP preview failed: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_activeLoadCancellation, cancellation))
            {
                _activeLoadCancellation = null;
                IsProcessing = false;
                NotifyAll();
            }
            cancellation.Dispose();
            choice.NotifyChanged();
        }
    }
    private async Task<StepPreviewMeshData> LoadMeshAsync(string path, CancellationToken cancellationToken)
    {
        await _previewLoadGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                if (!_preview.LoadModel(path)) throw new InvalidDataException("The STEP preview service could not load the model.");
                return _preview.ModelMesh ?? throw new InvalidDataException("The STEP preview service returned no model mesh.");
            });
        }
        finally { _previewLoadGate.Release(); }
    }
    private string SettingsPath(Guid documentId) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawVerify", "viewer-settings", $"{documentId:N}.json");
    private void RestoreSettings(Guid documentId)
    {
        var path = SettingsPath(documentId); if (!File.Exists(path)) return;
        try
        {
            var settings = JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path));
            if (settings is null || !settings.IsValid) throw new InvalidDataException("Viewer settings contain invalid camera or section values.");
            _suppressSettingsSave = true; CameraTarget = new Point3D(settings.TargetX, settings.TargetY, settings.TargetZ); CameraPosition = new Point3D(settings.PositionX, settings.PositionY, settings.PositionZ); CameraLookDirection = new Vector3D(settings.LookX, settings.LookY, settings.LookZ); CameraZoom = settings.Zoom; _isSectionEnabled = settings.SectionEnabled; _sectionAxis = settings.SectionAxis; _sectionPosition = settings.SectionPosition; _suppressSettingsSave = false;
            Notify(nameof(IsSectionEnabled)); Notify(nameof(SectionAxis)); Notify(nameof(SectionPosition));
        }
        catch (Exception exception) { _suppressSettingsSave = false; _logger?.Log(exception, "Restore STEP viewer settings", $"Unable to restore settings for documentId={documentId}; path='{path}'."); ProcessingStatus = "Viewer settings could not be restored."; }
    }
    private void SaveSettings()
    {
        if (_suppressSettingsSave || _documentId == Guid.Empty || !IsCameraAvailable) return;
        var path = SettingsPath(_documentId);
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var settings = new ViewerSettings(CameraTarget.X, CameraTarget.Y, CameraTarget.Z, CameraPosition.X, CameraPosition.Y, CameraPosition.Z, CameraLookDirection.X, CameraLookDirection.Y, CameraLookDirection.Z, CameraZoom, IsSectionEnabled, SectionAxis, SectionPosition); File.WriteAllText(path, JsonSerializer.Serialize(settings)); }
        catch (Exception exception) { _logger?.Log(exception, "Save STEP viewer settings", $"Unable to save settings for documentId={_documentId}; path='{path}'."); ProcessingStatus = "Viewer settings could not be saved."; }
    }
    private sealed record ViewerSettings(double TargetX, double TargetY, double TargetZ, double PositionX, double PositionY, double PositionZ, double LookX, double LookY, double LookZ, double Zoom, bool SectionEnabled = false, string SectionAxis = "Z", double SectionPosition = 0)
    { public bool IsValid => new[] { TargetX, TargetY, TargetZ, PositionX, PositionY, PositionZ, LookX, LookY, LookZ, Zoom, SectionPosition }.All(double.IsFinite) && Zoom is >= 0.05 and <= 20 && new Vector3D(LookX, LookY, LookZ).Length > 0 && SectionAxis is "X" or "Y" or "Z"; }
    public bool IsSectionEnabled { get => _isSectionEnabled; set { if (_isSectionEnabled == value) return; _isSectionEnabled = value; Notify(); RefreshSection(); } }
    public IReadOnlyList<string> SectionAxes { get; } = ["X", "Y", "Z"];
    public string SectionAxis { get => _sectionAxis; set { if (value is not ("X" or "Y" or "Z") || _sectionAxis == value) return; _sectionAxis = value; Notify(); RefreshSection(); } }
    public double SectionPosition { get => _sectionPosition; set { if (!double.IsFinite(value) || _sectionPosition.Equals(value)) return; _sectionPosition = value; Notify(); RefreshSection(); } }
    private void RefreshSection()
    {
        if (_selectedModel is null || !IsModelLoaded) return;
        try
        {
            var mesh = _preview.ModelMesh ?? throw new InvalidDataException("The STEP preview mesh is unavailable for sectioning.");
            _modelContent = BuildUploadedMesh(mesh); Notify(nameof(ModelContent)); SaveSettings();
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Update STEP viewer section", $"Unable to apply section settings for documentId={_documentId}; axis={SectionAxis}; position={SectionPosition}.");
            ProcessingStatus = "Viewer section could not be applied.";
        }
    }
    private Model3DGroup BuildUploadedMesh(StepPreviewMeshData data)
    {
        if (!IsSectionEnabled) return BuildMesh(data);
        var group = new Model3DGroup();
        foreach (var face in data.Triangles.GroupBy(triangle => triangle.FaceId))
        {
            var positions = new Point3DCollection(); var indices = new Int32Collection();
            foreach (var triangle in face)
            {
                var polygon = new List<Point3D>
                {
                    ToPoint(data.Vertices[triangle.First].Point), ToPoint(data.Vertices[triangle.Second].Point), ToPoint(data.Vertices[triangle.Third].Point)
                };
                polygon = ClipSectionPolygon(polygon);
                for (var i = 1; i + 1 < polygon.Count; i++)
                {
                    var start = positions.Count;
                    positions.Add(polygon[0]); positions.Add(polygon[i]); positions.Add(polygon[i + 1]);
                    indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                }
            }
            if (indices.Count == 0) continue;
            var material = new DiffuseMaterial(ComponentBrush(face.Key));
            group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = indices }, material) { BackMaterial = material });
        }
        return group;
    }
    private List<Point3D> ClipSectionPolygon(List<Point3D> polygon)
    {
        var clipped = new List<Point3D>();
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i]; var previous = polygon[(i + polygon.Count - 1) % polygon.Count];
            var currentDistance = SectionCoordinate(current) - SectionPosition; var previousDistance = SectionCoordinate(previous) - SectionPosition;
            var currentInside = currentDistance >= 0; var previousInside = previousDistance >= 0;
            if (currentInside != previousInside)
            {
                var ratio = previousDistance / (previousDistance - currentDistance);
                clipped.Add(new Point3D(previous.X + (current.X - previous.X) * ratio, previous.Y + (current.Y - previous.Y) * ratio, previous.Z + (current.Z - previous.Z) * ratio));
            }
            if (currentInside) clipped.Add(current);
        }
        return clipped;
    }
    private double SectionCoordinate(Point3D point) => SectionAxis switch { "X" => point.X, "Y" => point.Y, _ => point.Z };
    private static Point3D ToPoint(StepPreviewPoint point) => new(point.X, point.Y, point.Z);
    public InternalApprovalStamp? InternalApprovalStamp { get; private set; }
    public bool HasInternalApprovalStamp => InternalApprovalStamp is not null;
    public string ApprovalStampText => InternalApprovalStamp is null ? string.Empty : $"Internally Approved · {InternalApprovalStamp.ApproverDisplayName} · {InternalApprovalStamp.AppliedUtc:O} · Revision {InternalApprovalStamp.Revision} · {InternalApprovalStamp.StampId} · {InternalApprovalStamp.FingerprintReference} · {InternalApprovalStamp.VerificationStatus}";
    public void SetInternalApprovalStamp(InternalApprovalStamp? stamp) { stamp?.Validate(); InternalApprovalStamp = stamp; Notify(nameof(InternalApprovalStamp)); Notify(nameof(HasInternalApprovalStamp)); Notify(nameof(ApprovalStampText)); }
    public string? HighlightedFeatureReference { get; private set; }
    public double? HighlightedLocationX { get; private set; }
    public double? HighlightedLocationY { get; private set; }
    public bool IsModelLoaded => _modelContent is not null;
    public bool IsPreviewAvailable => !string.IsNullOrWhiteSpace(HighlightedFeatureReference);
    public bool IsFeatureResolved { get; private set; }
    public bool IsCameraAvailable => IsFinite(CameraPosition) && IsFinite(CameraLookDirection) && CameraLookDirection.Length > 0;
    public Model3DGroup? ModelContent => _modelContent;
    public Model3DGroup? HighlightContent => _highlightContent;
    public string PreviewStatus => IsFeatureResolved
        ? $"Highlighted STEP feature {HighlightedFeatureReference}."
        : IsModelLoaded
            ? "STEP model loaded. Select a STEP feature to highlight it."
            : "STEP preview unavailable: the model is not loaded.";
    public double CameraZoom { get; private set; } = 1;
    public Point3D CameraTarget { get; private set; }
    public Point3D CameraPosition { get; private set; } = new(0, 0, 250);
    public Vector3D CameraLookDirection { get; private set; } = new(0, 0, -250);
    public Vector3D CameraUpDirection { get; } = new(0, 1, 0);
    public string CameraStatus => !IsModelLoaded ? "3D camera navigation unavailable: the model is not loaded." : IsFeatureResolved ? $"STEP camera fitted to {HighlightedFeatureReference} (zoom {CameraZoom:0.##})." : IsCameraAvailable ? "STEP camera fitted to the loaded model." : "3D camera navigation unavailable.";
    public bool LoadModel(string path)
    {
        if (!_preview.LoadModel(path)) { ModelPath = null; _modelContent = null; NotifyAll(); return false; }
        ModelPath = path; _modelContent = BuildMesh(_preview.ModelMesh!);
        if (_modelContent.Children.Count == 0)
        {
            _logger?.Log(new InvalidOperationException("The STEP model produced no renderable triangles."), "STEP preview model load", $"Preview mesh was empty for path='{path}'.");
            ModelPath = null;
            _modelContent = null;
            NotifyAll();
            return false;
        }
        FitModel(_preview.ModelMesh!);
        NotifyAll(); return true;
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
    public void Orbit(double horizontalDegrees, double verticalDegrees)
    {
        if (!IsModelLoaded || !IsCameraAvailable) return;
        var offset = CameraPosition - CameraTarget;
        var yaw = Matrix3D.Identity; yaw.Rotate(new Quaternion(CameraUpDirection, horizontalDegrees)); offset = yaw.Transform(offset);
        var look = CameraTarget - CameraPosition;
        var right = Vector3D.CrossProduct(look, CameraUpDirection);
        if (right.Length > 1e-9)
        {
            right.Normalize();
            var pitch = Matrix3D.Identity; pitch.Rotate(new Quaternion(right, verticalDegrees)); offset = pitch.Transform(offset);
        }
        CameraPosition = CameraTarget + offset;
        CameraLookDirection = CameraTarget - CameraPosition;
        Notify(nameof(CameraPosition)); Notify(nameof(CameraLookDirection)); Notify(nameof(CameraStatus));
        SaveSettings();
    }
    public void Zoom(double factor)
    {
        if (!IsModelLoaded || !IsCameraAvailable || !double.IsFinite(factor) || factor <= 0) return;
        var offset = CameraPosition - CameraTarget;
        var distance = Math.Max(offset.Length * factor, 1e-6);
        offset.Normalize();
        CameraPosition = CameraTarget + offset * distance;
        CameraLookDirection = CameraTarget - CameraPosition;
        CameraZoom = Math.Max(0.05, Math.Min(20, CameraZoom / factor));
        Notify(nameof(CameraPosition)); Notify(nameof(CameraLookDirection)); Notify(nameof(CameraZoom)); Notify(nameof(CameraStatus));
        SaveSettings();
    }
    private static Model3DGroup BuildMesh(StepPreviewMeshData data, Brush? brush = null) => BuildMesh(data.Triangles, data, brush);
    private static Model3DGroup BuildMesh(IReadOnlyList<StepPreviewTriangle> triangles, StepPreviewMeshData data, Brush? brush = null)
    {
        var group = new Model3DGroup(); var positions = new Point3DCollection(data.Vertices.Select(v => new Point3D(v.Point.X, v.Point.Y, v.Point.Z)));
        var indices = new Int32Collection(triangles.SelectMany(t => new[] { t.First, t.Second, t.Third }));
        foreach (var face in triangles.GroupBy(triangle => triangle.FaceId))
        {
            var faceIndices = new Int32Collection(face.SelectMany(triangle => new[] { triangle.First, triangle.Second, triangle.Third }));
            if (faceIndices.Count == 0) continue;
            var material = new DiffuseMaterial(brush ?? ComponentBrush(face.Key));
            group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = faceIndices }, material) { BackMaterial = material });
        }
        return group;
    }
    private static Brush ComponentBrush(int faceId)
    {
        var colors = new[] { Color.FromRgb(74, 144, 226), Color.FromRgb(80, 190, 140), Color.FromRgb(235, 170, 72), Color.FromRgb(170, 120, 220), Color.FromRgb(70, 190, 200), Color.FromRgb(220, 105, 125) };
        var color = colors[Math.Abs(faceId == int.MinValue ? 0 : faceId) % colors.Length];
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
    private static Model3DGroup BuildEdgeHighlight(IReadOnlyList<StepPreviewPoint> points, Brush brush)
    {
        var group = new Model3DGroup(); if (points.Count < 2) return group; var positions = new Point3DCollection();
        for (var i = 0; i < points.Count; i++) { var point = new Point3D(points[i].X, points[i].Y, points[i].Z); var tangent = new Vector3D(points[Math.Min(i + 1, points.Count - 1)].X - points[Math.Max(i - 1, 0)].X, points[Math.Min(i + 1, points.Count - 1)].Y - points[Math.Max(i - 1, 0)].Y, points[Math.Min(i + 1, points.Count - 1)].Z - points[Math.Max(i - 1, 0)].Z); if (tangent.Length <= 1e-9) tangent = new Vector3D(1, 0, 0); else tangent.Normalize(); var up = Math.Abs(Vector3D.DotProduct(tangent, new Vector3D(0, 0, 1))) > .95 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1); var side = Vector3D.CrossProduct(tangent, up); side.Normalize(); var other = Vector3D.CrossProduct(tangent, side); other.Normalize(); positions.Add(point + side * .08 + other * .08); positions.Add(point - side * .08 + other * .08); positions.Add(point - side * .08 - other * .08); positions.Add(point + side * .08 - other * .08); }
        var indices = new List<int>(); for (var i = 0; i < points.Count - 1; i++) { var c = i * 4; var n = (i + 1) * 4; for (var s = 0; s < 4; s++) { var o = (s + 1) % 4; indices.AddRange(new[] { c + s, n + s, n + o, c + s, n + o, c + o }); } } if (indices.Count > 0) { var material = new DiffuseMaterial(brush); group.Children.Add(new GeometryModel3D(new MeshGeometry3D { Positions = positions, TriangleIndices = new Int32Collection(indices) }, material) { BackMaterial = material }); } return group;
    }
    private void FitModel(StepPreviewMeshData data)
    {
        var points = data.Vertices.Select(vertex => vertex.Point).Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)).ToArray();
        if (points.Length == 0) return;
        UpdateCamera(new StepPreviewBounds(points.Min(point => point.X), points.Min(point => point.Y), points.Min(point => point.Z), points.Max(point => point.X), points.Max(point => point.Y), points.Max(point => point.Z), false));
    }
    private void UpdateCamera(StepPreviewBounds b) { CameraTarget = new Point3D((b.MinimumX + b.MaximumX) / 2, (b.MinimumY + b.MaximumY) / 2, (b.MinimumZ + b.MaximumZ) / 2); var d = Math.Sqrt(Math.Pow(b.MaximumX - b.MinimumX, 2) + Math.Pow(b.MaximumY - b.MinimumY, 2) + Math.Pow(b.MaximumZ - b.MinimumZ, 2)); var distance = Math.Max(double.IsFinite(d) ? d * 2 / CameraZoom : 0, 1); CameraPosition = CameraTarget + new Vector3D(0, 0, distance); CameraLookDirection = CameraTarget - CameraPosition; }
    private static bool IsFinite(Point3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z); private static bool IsFinite(Vector3D v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    public void Dispose() { CancelProcessing(); _previewLoadGate.Wait(); _previewLoadGate.Release(); _preview.Dispose(); GC.SuppressFinalize(this); }
    private void NotifyAll() { foreach (var n in new[] { nameof(ModelPath), nameof(IsModelLoaded), nameof(HighlightedFeatureReference), nameof(HighlightedLocationX), nameof(HighlightedLocationY), nameof(IsPreviewAvailable), nameof(IsFeatureResolved), nameof(IsCameraAvailable), nameof(ModelContent), nameof(HighlightContent), nameof(PreviewStatus), nameof(CameraZoom), nameof(CameraTarget), nameof(CameraPosition), nameof(CameraLookDirection), nameof(CameraUpDirection), nameof(CameraStatus), nameof(Models) }) Notify(n); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class StepPreviewModelChoice(Guid documentId, string name, string path) : INotifyPropertyChanged
{
    private bool _isLoaded;
    private string _error = string.Empty;
    public Guid DocumentId { get; } = documentId;
    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsLoaded { get => _isLoaded; set { _isLoaded = value; NotifyChanged(nameof(IsLoaded)); } }
    public string Error { get => _error; set { _error = value; NotifyChanged(nameof(Error)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyChanged(string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public override string ToString() => string.IsNullOrEmpty(Error) ? Name : $"{Name} — {Error}";
}
