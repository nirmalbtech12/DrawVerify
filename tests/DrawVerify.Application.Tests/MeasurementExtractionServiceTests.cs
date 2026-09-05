using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MeasurementExtractionServiceTests
{
    [Fact(DisplayName = "TD-APP-001 authorized Maker extraction persists and audits success")]
    public void Maker_extraction_processes_uploaded_step_and_writes_audit()
    {
        var document = Document();
        var version = new DocumentVersion { DocumentId = document.Id, StoredFilePath = "model.step" };
        var repository = new FakeRepository(document, version);
        var audit = new FakeAudit();
        var geometry = new FakeGeometry(new MeasurementExtractionResult(true,
            new GeometryData { Volume = 10, BoundingBox = new BoundingBox(0, 0, 0, 1, 2, 3), FaceCount = 6, EdgeCount = 12, VertexCount = 8 }, Array.Empty<FeatureData>()));
        var service = new MeasurementExtractionService(repository, audit, new FakeAuth(new User { Role = "Maker", IsAuthenticated = true }), geometry);

        var result = service.Extract(document.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(GeometryStatus.Processed, repository.Status);
        Assert.Equal("MeasurementsExtracted", Assert.Single(audit.Entries).Action);
        Assert.Contains("durationMilliseconds", audit.Entries[0].DetailsJson);
        Assert.Equal(1, geometry.Calls);
    }

    [Fact(DisplayName = "TD-APP-001 denied users cannot invoke extraction")]
    public void Unauthorized_user_is_rejected_before_geometry_call()
    {
        var document = Document();
        var repository = new FakeRepository(document, new DocumentVersion { DocumentId = document.Id });
        var geometry = new FakeGeometry(MeasurementExtractionResult.Failure("x", "x"));
        var service = new MeasurementExtractionService(repository, new FakeAudit(), new FakeAuth(null), geometry);

        Assert.Throws<UnauthorizedAccessException>(() => service.Extract(document.Id));
        Assert.Equal(0, geometry.Calls);
        Assert.Equal(0, repository.Replacements);
    }

    [Fact(DisplayName = "TD-APP-001 re-extraction replaces same version and records failure")]
    public void Non_solid_result_sets_error_and_keeps_partial_geometry()
    {
        var document = Document();
        var version = new DocumentVersion { DocumentId = document.Id };
        var repository = new FakeRepository(document, version);
        var audit = new FakeAudit();
        var service = new MeasurementExtractionService(repository, audit, new FakeAuth(new User { Role = "Checker", IsAuthenticated = true }),
            new FakeGeometry(MeasurementExtractionResult.Failure("NonSolid", "Model does not contain a valid solid; volume cannot be computed.", new GeometryData { FaceCount = 1 })));

        service.Extract(document.Id);

        Assert.Equal(GeometryStatus.Error, repository.Status);
        Assert.Equal("MeasurementExtractionFailed", Assert.Single(audit.Entries).Action);
        Assert.Null(repository.Geometry!.Volume);
        Assert.Equal(version.Id, repository.ReplacedVersionId);
    }

    private static Document Document() => new() { DocumentType = DocumentType.StepModel, UploadStatus = UploadStatus.Uploaded };

    private sealed class FakeGeometry(MeasurementExtractionResult result) : DrawVerify.Geometry.IGeometryService
    { public int Calls; public MeasurementExtractionResult ExtractMeasurements(MeasurementExtractionRequest request, CancellationToken cancellationToken = default) { Calls++; return result; } }
    private sealed class FakeAuth(User? user) : IAuthenticationService { public User? CurrentUser => user; public bool CanExtractMeasurements => user is not null; }
    private sealed class FakeAudit : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public void Append(AuditEntry entry) => Entries.Add(entry); }
    private sealed class FakeRepository(Document document, DocumentVersion version) : IDocumentRepository
    {
        public GeometryStatus Status { get; private set; } = GeometryStatus.NotExtracted; public int Replacements; public Guid ReplacedVersionId; public GeometryMeasurement? Geometry;
        public Document? Get(Guid id) => id == document.Id ? document : null;
        public DocumentVersion? GetCurrentVersion(Guid id) => id == document.Id ? version : null;
        public void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, GeometryStatus status, string? error) { Replacements++; ReplacedVersionId = versionId; Geometry = geometry; Status = status; version.GeometryStatus = status; version.GeometryError = error; }
    }
}
