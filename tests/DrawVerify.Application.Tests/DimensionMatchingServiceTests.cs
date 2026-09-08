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

using System.Text.Json;

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

    [Fact]
    public async Task Unmatched_dimensions_are_persisted_with_reason_breakdown_and_audit_details()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var dimensions = new[]
        {
            new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10, displayText: "10 mm"),
            new CoreDimensionData(dxf.DocumentId, dxf.Version.VersionId, 2, DimensionType.Linear, 99, displayText: "99 mm")
        };
        var documents = new DocumentsFake(step, dxf, dimensions);
        var sessions = new SessionFake(session);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker-id" });
        var geometry = new GeometryFake(new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 10 });
        var service = new DimensionMatchingService(documents, sessions, geometry, auth, new DimensionMatchingConfiguration());

        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);

        Assert.Equal(DocumentAuditAction.UnmatchedDimensionsFlagged, sessions.Action);
        var unmatched = Assert.Single(sessions.Matches, x => x.Status == DimensionMatchStatus.Unmatched);
        Assert.Equal("NoCorrespondingGeometryFound", unmatched.ReasonCode);
        Assert.Equal(DimensionType.Linear, unmatched.DimensionType);
        Assert.Equal(99, unmatched.NominalValue);
        Assert.Equal("99 mm", unmatched.DimensionText);
        using var details = JsonDocument.Parse(sessions.DetailsJson!);
        Assert.Equal(session.VerificationSessionId, details.RootElement.GetProperty("verificationSessionId").GetGuid());
        Assert.Equal("checker-id", sessions.UserId);
        Assert.Equal(1, details.RootElement.GetProperty("unmatchedDimensions").GetInt32());
        Assert.Equal(1, details.RootElement.GetProperty("reasonBreakdown").GetProperty("NoCorrespondingGeometryFound").GetInt32());
    }

    [Fact]
    public async Task Result_retrieval_authorizes_before_filtering_and_returns_requested_status()
    {
        var sessions = new SessionFake { Matches = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 1 },
            new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 2, ReasonCode = "AmbiguousReference" }
        }};
        var auth = new SessionAuthenticationService();
        var service = new DimensionMatchingService(new DocumentsFake(), sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetResultsAsync(Guid.NewGuid(), DimensionMatchStatus.Unmatched));
        Assert.Equal(0, sessions.MatchLookupCount);

        auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }));
        var unmatched = await service.GetResultsAsync(Guid.NewGuid(), DimensionMatchStatus.Unmatched);
        Assert.Single(unmatched);
        Assert.Equal(DimensionMatchStatus.Unmatched, unmatched[0].Status);
        Assert.Equal(1, sessions.MatchLookupCount);
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
        private readonly VerificationSession _session = session ?? new VerificationSession(); public int LookupCount; public int ReplaceCount; public int MatchLookupCount; public DocumentAuditAction Action; public string? UserId; public string? DetailsJson; public IReadOnlyList<DimensionMatch> Matches = Array.Empty<DimensionMatch>();
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) { LookupCount++; return Task.FromResult<VerificationSession?>(_session with { VerificationSessionId = id }); }
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken cancellationToken = default) => Task.FromResult(_session);
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) { MatchLookupCount++; return Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? Matches : Matches.Where(x => x.Status == status).ToArray()); }
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken cancellationToken = default) { ReplaceCount++; Action = action; UserId = userId; DetailsJson = detailsJson; Matches = matches.ToArray(); return Task.CompletedTask; }
    }
    private sealed class GeometryFake(params GeometryFeatureData[] features) : IMatchableGeometryService
    { public int CallCount; public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default) { CallCount++; return new(true, features); } }
}
