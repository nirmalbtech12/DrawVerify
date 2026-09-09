using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using DrawVerify.Geometry;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DimensionMatchingServiceStatusTests
{
    [Fact(DisplayName = "TD-APP-005 refreshes persisted statuses after tolerance re-comparison")]
    public async Task Recomparison_replaces_stale_statuses()
    {
        var step = Document(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = Document(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var dimensions = new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10) };
        var sessions = new Sessions(session); var geometry = new Geometry(new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 9.85 });
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }));
        var service = new DimensionMatchingService(new Documents(step, dxf, dimensions), sessions, geometry, auth, new DimensionMatchingConfiguration());

        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);
        Assert.Equal(DimensionMatchStatus.Mismatched, Assert.Single(sessions.Matches).Status);
        geometry.Features = new[] { new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 10 } };
        await service.UpdateToleranceAndRecompareAsync(session.VerificationSessionId, .1);

        Assert.Equal(DimensionMatchStatus.Matched, Assert.Single(sessions.Matches).Status);
        Assert.Equal(2, sessions.ReplaceCount);
    }

    private static Document Document(CoreDocumentType type, CoreGeometryStatus geometry, VerificationStatus verification)
    { var id = Guid.NewGuid(); var version = new DocumentVersion { DocumentId = id, VersionNumber = 1, StoredRelativePath = "fixture.step", FileSizeBytes = 10 }; return new Document { DocumentId = id, DocumentType = type, GeometryStatus = geometry, VerificationStatus = verification, Version = version, Versions = new[] { version } }; }
    private sealed class Documents(Document step, Document dxf, IReadOnlyList<CoreDimensionData> dimensions) : IDocumentRepository
    { public Task<Document?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<Document?>(id == step.DocumentId ? step : id == dxf.DocumentId ? dxf : null); public Document? Get(Guid id) => id == step.DocumentId ? step : id == dxf.DocumentId ? dxf : null; public DocumentVersion? GetCurrentVersion(Guid id) => Get(id)?.Version; public Task<IReadOnlyList<CoreDimensionData>> GetDimensionsAsync(Guid id, CancellationToken token = default) => Task.FromResult(dimensions); }
    private sealed class Geometry(params GeometryFeatureData[] initial) : IMatchableGeometryService
    { public GeometryFeatureData[] Features { get; set; } = initial; public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken token = default) => new(true, Features); }
    private sealed class Sessions(VerificationSession session) : IVerificationSessionRepository
    { public int ReplaceCount; public IReadOnlyList<DimensionMatch> Matches { get; private set; } = Array.Empty<DimensionMatch>(); public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(session with { VerificationSessionId = id }); public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => Task.FromResult(session); public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? Matches : Matches.Where(match => match.Status == status).ToArray()); public Task ReplaceResultsAsync(VerificationSession s, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) { ReplaceCount++; Matches = matches.ToArray(); return Task.CompletedTask; } }
}
