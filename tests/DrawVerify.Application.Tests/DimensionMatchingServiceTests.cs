using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.Application.Tests;

public sealed class DimensionMatchingServiceTests
{
    [Fact]
    public async Task Unauthorized_matching_does_not_lookup_session_or_geometry()
    {
        var sessions = new SessionFake();
        var geometry = new GeometryFake();
        var auth = new SessionAuthenticationService();
        var service = new DimensionMatchingService(new DocumentsFake(), sessions, geometry, auth, new DimensionMatchingConfiguration());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MatchDimensionsToGeometryAsync(Guid.NewGuid()));
        Assert.Equal(0, sessions.LookupCount);
        Assert.Equal(0, geometry.CallCount);
    }

    [Fact]
    public async Task Authorized_processed_pair_persists_match_and_audit_action()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var documents = new DocumentsFake(step, dxf, new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10) });
        var sessions = new SessionFake(session);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }) { UserId = "maker" });
        var geometry = new GeometryFake(new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 10 });
        var service = new DimensionMatchingService(documents, sessions, geometry, auth, new DimensionMatchingConfiguration());

        var result = await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);

        Assert.Equal(VerificationSessionStatus.DimensionsMatched, result.Status);
        Assert.Equal(DocumentAuditAction.DimensionsMatched, sessions.Action);
        Assert.Single(sessions.Matches);
        Assert.Equal(1, geometry.CallCount);
    }

    private static Document NewDocument(CoreDocumentType type, CoreGeometryStatus geometry, VerificationStatus verification)
    {
        var id = Guid.NewGuid(); var version = new DocumentVersion { DocumentId = id, VersionNumber = 1, StoredRelativePath = "fixture.step", FileSizeBytes = 10 };
        return new Document { DocumentId = id, DocumentType = type, GeometryStatus = geometry, VerificationStatus = verification, Version = version, Versions = new[] { version } };
    }

    private sealed class DocumentsFake(params Document[] documents) : IDocumentRepository
    {
        private readonly Dictionary<Guid, Document> _documents = documents.ToDictionary(x => x.DocumentId);
        private readonly IReadOnlyList<CoreDimensionData> _dimensions = Array.Empty<CoreDimensionData>();
        public DocumentsFake(Document step, Document dxf, IReadOnlyList<CoreDimensionData> dimensions) : this(step, dxf) => _dimensions = dimensions;
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_documents.GetValueOrDefault(id));
        public Document? Get(Guid id) => _documents.GetValueOrDefault(id);
        public DocumentVersion? GetCurrentVersion(Guid id) => Get(id)?.Version;
        public Task<IReadOnlyList<CoreDimensionData>> GetDimensionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_dimensions);
    }
    private sealed class SessionFake(VerificationSession? session = null) : IVerificationSessionRepository
    {
        private readonly VerificationSession _session = session ?? new VerificationSession(); public int LookupCount; public int ReplaceCount; public DocumentAuditAction Action; public IReadOnlyList<DimensionMatch> Matches = Array.Empty<DimensionMatch>();
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) { LookupCount++; return Task.FromResult<VerificationSession?>(_session with { VerificationSessionId = id }); }
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken cancellationToken = default) => Task.FromResult(_session);
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult(Matches);
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken cancellationToken = default) { ReplaceCount++; Action = action; Matches = matches.ToArray(); return Task.CompletedTask; }
    }
    private sealed class GeometryFake(params GeometryFeatureData[] features) : IMatchableGeometryService
    { public int CallCount; public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default) { CallCount++; return new(true, features); } }
}
