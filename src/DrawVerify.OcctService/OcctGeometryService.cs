using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using DrawVerify.Occt;

namespace DrawVerify.OcctService;

/// <summary>
/// The ONLY layer that knows OCCT. Implements IGeometryService using the SWIG
/// wrapper (DrawVerify.OcctWrapper) and maps results into Geometry DTOs.
/// </summary>
public sealed class OcctGeometryService : IGeometryService, IMatchableGeometryService
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
                    features.Add(new GeometryFeatureData { Reference = $"Face:{face.Id}", GeometryType = face.SurfaceType, Height = double.IsFinite(height) && height > 0 ? height : null });
                }
                if (string.Equals(face.SurfaceType, "Cylinder", StringComparison.OrdinalIgnoreCase) && double.IsFinite(face.Radius) && face.Radius > 0)
                    features.Add(new GeometryFeatureData { Reference = $"Face:{face.Id}:Cylinder", GeometryType = face.Orientation == -1 ? "Hole" : "CylindricalFace", Radius = face.Radius, Diameter = face.Radius * 2 });
            }
            return new MatchableGeometryExtractionResult(true, features);
        }
        catch (OcctOperationException ex) { return MatchableGeometryExtractionResult.Failure(ex.ErrorCode, ex.Message); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { return MatchableGeometryExtractionResult.Failure(ex.GetType().Name, ex.Message); }
    }

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
