using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using Xunit;
using GeometryStatus = DrawVerify.Core.Enums.GeometryStatus;
using DocumentType = DrawVerify.Core.Enums.DocumentType;
using UploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.Application.Tests;

public sealed class MeasurementExtractionServiceTests
{
    [Fact(DisplayName = "TD-KAN116-APP-002 extraction persists and audits success")]
    public void Extraction_processes_uploaded_step_and_writes_audit()
    {
        var document = Document();
        var version = new DocumentVersion { DocumentId = document.Id, StoredFilePath = "model.step" };
        var repository = new FakeRepository(document, version);
        var audit = new FakeAudit();
        var geometry = new FakeGeometry(new MeasurementExtractionResult(true,
            new GeometryData { Volume = 10, BoundingBox = new BoundingBox(0, 0, 0, 1, 2, 3), FaceCount = 6, EdgeCount = 12, VertexCount = 8 }, Array.Empty<FeatureData>()));
        var service = new MeasurementExtractionService(repository, audit, geometry);

        var result = service.Extract(document.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(GeometryStatus.Processed, repository.Status);
        Assert.Equal(DocumentAuditAction.MeasurementsExtracted, Assert.Single(audit.Entries).Action);
        Assert.Contains("durationMilliseconds", audit.Entries[0].DetailsJson);
        Assert.Equal(1, geometry.Calls);
    }

    [Fact(DisplayName = "TD-KAN116-APP-002 extraction failure persists without authorization dependency")]
    public void Non_solid_result_sets_error_and_keeps_partial_geometry()
    {
        var document = Document();
        var version = new DocumentVersion { DocumentId = document.Id };
        var repository = new FakeRepository(document, version);
        var audit = new FakeAudit();
        var service = new MeasurementExtractionService(repository, audit,
            new FakeGeometry(MeasurementExtractionResult.Failure("NonSolid", "Model does not contain a valid solid; volume cannot be computed.", new GeometryData { FaceCount = 1 })));

        service.Extract(document.Id);

        Assert.Equal(GeometryStatus.Error, repository.Status);
        Assert.Equal(DocumentAuditAction.MeasurementExtractionFailed, Assert.Single(audit.Entries).Action);
        Assert.Null(repository.Geometry!.Volume);
        Assert.Equal(version.Id, repository.ReplacedVersionId);
    }

    private static Document Document() => new() { DocumentType = DocumentType.StepModel, UploadStatus = UploadStatus.Uploaded };

    private sealed class FakeGeometry(MeasurementExtractionResult result) : DrawVerify.Geometry.IGeometryService
    { public int Calls; public MeasurementExtractionResult ExtractMeasurements(MeasurementExtractionRequest request, CancellationToken cancellationToken = default) { Calls++; return result; } }
    private sealed class FakeAudit : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public void Append(AuditEntry entry) => Entries.Add(entry); }
    private sealed class FakeRepository(Document document, DocumentVersion version) : IDocumentRepository
    {
        public GeometryStatus Status { get; private set; } = GeometryStatus.NotExtracted; public int Replacements; public Guid ReplacedVersionId; public GeometryMeasurement? Geometry;
        public Document? Get(Guid id) => id == document.Id ? document : null;
        public DocumentVersion? GetCurrentVersion(Guid id) => id == document.Id ? version : null;
        public void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, GeometryStatus status, string? error) { Replacements++; ReplacedVersionId = versionId; Geometry = geometry; Status = status; version.GeometryStatus = status; version.GeometryError = error; }
    }
}
