using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public sealed class MeasurementExtractionService
{
    private readonly IDocumentRepository _documents;
    private readonly IAuditLogger _audit;
    private readonly IAuthenticationService _authentication;
    private readonly DrawVerify.Geometry.IGeometryService _geometry;

    public MeasurementExtractionService(IDocumentRepository documents, IAuditLogger audit, IAuthenticationService authentication, DrawVerify.Geometry.IGeometryService geometry)
        => (_documents, _audit, _authentication, _geometry) = (documents, audit, authentication, geometry);

    public MeasurementExtractionResult Extract(Guid documentId, CancellationToken cancellationToken = default)
    {
        var user = _authentication.CurrentUser;
        if (user is null || !IsAllowed(user.Role))
            throw new UnauthorizedAccessException("Only authenticated Maker or Checker users may extract measurements.");
        var document = _documents.Get(documentId) ?? throw new InvalidOperationException("Document was not found.");
        if (document.DocumentType != DrawVerify.Core.Enums.DocumentType.StepModel || document.UploadStatus != DrawVerify.Core.Enums.UploadStatus.Uploaded)
            throw new InvalidOperationException("Measurement extraction requires an uploaded STEP model.");
        var version = _documents.GetCurrentVersion(documentId) ?? throw new InvalidOperationException("Document version was not found.");
        var stopwatch = Stopwatch.StartNew();
        MeasurementExtractionResult result;
        try { result = _geometry.ExtractMeasurements(new MeasurementExtractionRequest(version.StoredFilePath), cancellationToken); }
        catch (OperationCanceledException) { result = MeasurementExtractionResult.Failure("Timeout", "Measurement extraction failed: operation exceeded the configured timeout."); }
        catch (Exception ex) { result = MeasurementExtractionResult.Failure("Geometry", $"Measurement extraction failed: {ex.Message}"); }
        stopwatch.Stop();
        var status = result.Succeeded ? DrawVerify.Core.Enums.GeometryStatus.Processed : DrawVerify.Core.Enums.GeometryStatus.Error;
        var geometry = result.Geometry is null ? null : new GeometryMeasurement(version.Id, result.Geometry.Volume,
            result.Geometry.BoundingBox?.MinimumX ?? 0, result.Geometry.BoundingBox?.MinimumY ?? 0, result.Geometry.BoundingBox?.MinimumZ ?? 0,
            result.Geometry.BoundingBox?.MaximumX ?? 0, result.Geometry.BoundingBox?.MaximumY ?? 0, result.Geometry.BoundingBox?.MaximumZ ?? 0,
            result.Geometry.FaceCount, result.Geometry.EdgeCount, result.Geometry.VertexCount, result.Geometry.Unit, result.Geometry.Uncertainty);
        var features = result.Features.Select(feature => new FeatureMeasurement(version.Id, feature.FeatureType, feature.NominalDimensions, feature.Unit, feature.LowerTolerance, feature.UpperTolerance, feature.Uncertainty)).ToArray();
        var error = result.ErrorMessage;
        _documents.ReplaceMeasurements(version.Id, geometry, features, status, error);
        document.GeometryStatus = status;
        document.GeometryError = error;
        var details = JsonSerializer.Serialize(new { durationMilliseconds = stopwatch.ElapsedMilliseconds, error, result.Geometry, featureCount = features.Length });
        _audit.Append(new AuditEntry { DocumentId = documentId, UserId = user.Id, Action = result.Succeeded ? DocumentAuditAction.MeasurementsExtracted : DocumentAuditAction.MeasurementExtractionFailed, DetailsJson = details });
        return result;
    }

    private static bool IsAllowed(string role) => string.Equals(role, "Maker", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Checker", StringComparison.OrdinalIgnoreCase);
}
