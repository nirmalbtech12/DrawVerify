using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using DrawVerify.Occt;

namespace DrawVerify.OcctService;

/// <summary>
/// The ONLY layer that knows OCCT. Implements IGeometryService using the SWIG
/// wrapper (DrawVerify.OcctWrapper) and maps results into Geometry DTOs.
/// </summary>
public sealed class OcctGeometryService : IGeometryService, IMatchableGeometryService, IProjectionDrawingService
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

    public MeasurementExtractionResult ExtractMeasurements(MeasurementExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var model = _loader.LoadStep(request.FilePath);
            if (model.IsNull) return MeasurementExtractionResult.Failure("InvalidModel", "The STEP model is null.");
            var summary = model.GetSummary();
            BoundingBox? box = null;
            var nativeBox = model.GetBoundingBox();
            if (!nativeBox.IsVoid && ValuesAreFinite(nativeBox))
                box = new BoundingBox(nativeBox.MinimumX, nativeBox.MinimumY, nativeBox.MinimumZ, nativeBox.MaximumX, nativeBox.MaximumY, nativeBox.MaximumZ);
            var geometry = new GeometryData { Volume = summary.SolidCount > 0 && double.IsFinite(summary.Volume) && summary.Volume > 0 ? summary.Volume : null, BoundingBox = box, FaceCount = summary.FaceCount, EdgeCount = summary.EdgeCount, VertexCount = summary.VertexCount, Unit = string.IsNullOrWhiteSpace(summary.Unit) ? "mm" : summary.Unit };
            if (summary.SolidCount <= 0) return MeasurementExtractionResult.Failure("NonSolid", "Model does not contain a valid solid; volume cannot be computed.", geometry);
            if (geometry.Volume is null) return MeasurementExtractionResult.Failure("InvalidVolume", "Model volume is not a finite positive value.", geometry);
            var features = DetectFeatures(model.GetFaces(), out var uncertainty);
            geometry.Uncertainty = uncertainty;
            return new MeasurementExtractionResult(true, geometry, features);
        }
        catch (OcctOperationException ex)
        {
            return MeasurementExtractionResult.Failure(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return MeasurementExtractionResult.Failure(ex.GetType().Name, ex.Message);
        }
    }

    public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var model = _loader.LoadStep(request.FilePath);
            if (model.IsNull) return MatchableGeometryExtractionResult.Failure("InvalidModel", "The STEP model is null.");
            var features = new List<GeometryFeatureData>();
            foreach (var edge in model.GetEdges())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (double.IsFinite(edge.Length) && edge.Length > 0)
                    features.Add(new GeometryFeatureData { Reference = $"Edge:{edge.Id}", GeometryType = edge.GeometryType, Length = edge.Length });
            }
            foreach (var face in model.GetFaces())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (double.IsFinite(face.Area) && face.Area > 0)
                {
                    var height = Math.Abs(face.BoundingBox.MaximumZ - face.BoundingBox.MinimumZ);
                    features.Add(new GeometryFeatureData { Reference = $"Face:{face.Id}", GeometryType = face.SurfaceType, Height = double.IsFinite(height) && height > 0 ? height : null, AngleDegrees = double.IsFinite(face.SemiAngle) && Math.Abs(face.SemiAngle) > 1e-12 ? Math.Abs(face.SemiAngle) * 180d / Math.PI : null });
                }
                if (string.Equals(face.SurfaceType, "Cylinder", StringComparison.OrdinalIgnoreCase) && double.IsFinite(face.Radius) && face.Radius > 0)
                    features.Add(new GeometryFeatureData { Reference = $"Face:{face.Id}:Cylinder", GeometryType = face.Orientation == -1 ? "Hole" : "CylindricalFace", Radius = face.Radius, Diameter = face.Radius * 2 });
            }
            return new MatchableGeometryExtractionResult(true, features);
        }
        catch (OcctOperationException ex) { return MatchableGeometryExtractionResult.Failure(ex.ErrorCode, ex.Message); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { return MatchableGeometryExtractionResult.Failure(ex.GetType().Name, ex.Message); }
    }

    public ProjectionDrawing Generate(string filePath, ProjectionDrawingView view, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var model = _loader.LoadStep(filePath);
            if (model.IsNull) throw new OcctOperationException("InvalidModel", "The STEP model is null.");
            var nativeView = view switch
            {
                ProjectionDrawingView.Front => OcctProjectionView.Front,
                ProjectionDrawingView.Rear => OcctProjectionView.Rear,
                ProjectionDrawingView.Top => OcctProjectionView.Top,
                ProjectionDrawingView.Bottom => OcctProjectionView.Bottom,
                ProjectionDrawingView.Left => OcctProjectionView.Left,
                ProjectionDrawingView.Right => OcctProjectionView.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(view))
            };
            var projection = model.GenerateProjection(nativeView);
            var entities = new List<ProjectionDrawingEntity>();
            var diagnostics = new List<string>();
            AddLines(entities, diagnostics, projection.VisibleLines, false); AddLines(entities, diagnostics, projection.HiddenLines, true);
            AddArcs(entities, diagnostics, projection.VisibleArcs, false); AddArcs(entities, diagnostics, projection.HiddenArcs, true);
            AddCircles(entities, diagnostics, projection.VisibleCircles, false); AddCircles(entities, diagnostics, projection.HiddenCircles, true);
            AddPolylines(entities, diagnostics, projection.VisiblePolylines, false); AddPolylines(entities, diagnostics, projection.HiddenPolylines, true);
            if (entities.Count == 0) diagnostics.Add("The selected STEP projection produced no supported geometric entities.");
            var dimensions = entities.Select((entity, index) => entity.Type switch
            {
                ProjectionDrawingEntityType.Line when entity.Coordinates.Count >= 4 => new ProjectionDrawingDimension("Linear", Math.Sqrt(Math.Pow(entity.Coordinates[2] - entity.Coordinates[0], 2) + Math.Pow(entity.Coordinates[3] - entity.Coordinates[1], 2)), entity.Coordinates[0], entity.Coordinates[1]),
                ProjectionDrawingEntityType.Circle when entity.Coordinates.Count >= 3 => new ProjectionDrawingDimension("Diameter", entity.Coordinates[2] * 2, entity.Coordinates[0], entity.Coordinates[1]),
                ProjectionDrawingEntityType.Arc when entity.Coordinates.Count >= 3 => new ProjectionDrawingDimension("Radius", entity.Coordinates[2], entity.Coordinates[0], entity.Coordinates[1]),
                _ => null
            }).Where(dimension => dimension is not null).Cast<ProjectionDrawingDimension>().ToArray();
            return new ProjectionDrawing(view, entities, dimensions, projection.ApproximationTolerance, diagnostics);
        }
        catch (OcctOperationException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException($"Projection generation failed for {view}: {exception.Message}", exception); }
    }

    private static void AddLines(List<ProjectionDrawingEntity> target, List<string> diagnostics, IReadOnlyList<OcctLine2> lines, bool hidden)
    { for (var index = 0; index < lines.Count; index++) { var line = lines[index]; if (Finite(line.StartX, line.StartY, line.EndX, line.EndY)) target.Add(new(ProjectionDrawingEntityType.Line, new[] { line.StartX, line.StartY, line.EndX, line.EndY }, hidden)); else diagnostics.Add($"Projection entity ordinal {index + 1} type LINE was omitted because it contains non-finite coordinates."); } }
    private static void AddArcs(List<ProjectionDrawingEntity> target, List<string> diagnostics, IReadOnlyList<OcctArc2> arcs, bool hidden)
    { for (var index = 0; index < arcs.Count; index++) { var arc = arcs[index]; if (Finite(arc.CenterX, arc.CenterY, arc.Radius, arc.StartParameter, arc.EndParameter) && arc.Radius > 0) target.Add(new(ProjectionDrawingEntityType.Arc, new[] { arc.CenterX, arc.CenterY, arc.Radius, arc.StartParameter, arc.EndParameter }, hidden)); else diagnostics.Add($"Projection entity ordinal {index + 1} type ARC was omitted because its geometry is invalid."); } }
    private static void AddCircles(List<ProjectionDrawingEntity> target, List<string> diagnostics, IReadOnlyList<OcctCircle2> circles, bool hidden)
    { for (var index = 0; index < circles.Count; index++) { var circle = circles[index]; if (Finite(circle.CenterX, circle.CenterY, circle.Radius) && circle.Radius > 0) target.Add(new(ProjectionDrawingEntityType.Circle, new[] { circle.CenterX, circle.CenterY, circle.Radius }, hidden)); else diagnostics.Add($"Projection entity ordinal {index + 1} type CIRCLE was omitted because its geometry is invalid."); } }
    private static void AddPolylines(List<ProjectionDrawingEntity> target, List<string> diagnostics, IReadOnlyList<OcctPolyline2> polylines, bool hidden)
    { for (var index = 0; index < polylines.Count; index++) { var coordinates = polylines[index].Points.SelectMany(point => new[] { point.X, point.Y }).ToArray(); if (coordinates.Length >= 4 && coordinates.All(double.IsFinite)) target.Add(new(ProjectionDrawingEntityType.Polyline, coordinates, hidden)); else diagnostics.Add($"Projection entity ordinal {index + 1} type POLYLINE was omitted because it contains invalid or insufficient points."); } }
    private static bool Finite(params double[] values) => values.All(double.IsFinite);

    private static bool ValuesAreFinite(OcctBoundingBox box) =>
        double.IsFinite(box.MinimumX) && double.IsFinite(box.MinimumY) && double.IsFinite(box.MinimumZ) &&
        double.IsFinite(box.MaximumX) && double.IsFinite(box.MaximumY) && double.IsFinite(box.MaximumZ);

    private static IReadOnlyList<FeatureData> DetectFeatures(IReadOnlyList<OcctFace> faces, out string? uncertainty)
    {
        var features = new List<FeatureData>();
        var uncertain = 0;
        foreach (var face in faces)
        {
            if (!string.Equals(face.SurfaceType, "Cylinder", StringComparison.OrdinalIgnoreCase) || !double.IsFinite(face.Radius) || face.Radius <= 0) continue;
            var height = Math.Abs(face.BoundingBox.MaximumZ - face.BoundingBox.MinimumZ);
            if (!double.IsFinite(height) || height <= 0) continue;
            var type = face.Orientation switch { -1 => "Hole", 1 => "Boss", _ => null };
            if (type is null) { uncertain++; continue; }
            features.Add(new FeatureData { FeatureType = type, NominalDimensions = new Dictionary<string, double> { ["Diameter"] = face.Radius * 2, ["Height"] = height } });
        }
        uncertainty = uncertain == 0 ? null : $"{uncertain} cylindrical candidate(s) were ambiguous and were not emitted as features.";
        return features;
    }
}
