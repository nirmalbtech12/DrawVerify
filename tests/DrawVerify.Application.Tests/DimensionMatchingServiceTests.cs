using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Application.Verification;
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
    [Fact(DisplayName = "TD-APP-EXTRA-002 persists extra results, summary counts, and audit fields")]
    public async Task Matching_persists_step_extra_and_canonical_audit_fields()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var dimensions = new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10) };
        var sessions = new SessionFake(session); var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var service = new DimensionMatchingService(new DocumentsFake(step, dxf, dimensions), sessions, new GeometryFake(new GeometryFeatureData { Reference = "matched", Length = 10 }, new GeometryFeatureData { Reference = "extra", GeometryType = "Hole", Diameter = 20 }), auth, new DimensionMatchingConfiguration());
        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);
        Assert.Single(sessions.Matches, match => match.Status == DimensionMatchStatus.ExtraDimension);
        using var details = JsonDocument.Parse(sessions.DetailsJson!);
        Assert.Equal(1, details.RootElement.GetProperty("extraStepCount").GetInt32()); Assert.Equal(step.DocumentId, details.RootElement.GetProperty("stepDocumentId").GetGuid());
        Assert.Equal(dxf.DocumentId, details.RootElement.GetProperty("dxfDocumentId").GetGuid()); Assert.Equal("checker", details.RootElement.GetProperty("triggeredBy").GetString());
    }

    [Fact(DisplayName = "TD-APP-EXTRA-005 enforces extra viewing and mutation authorization")]
    public async Task Extra_result_queries_and_mutations_follow_role_permissions()
    {
        var id = Guid.NewGuid(); var matchId = Guid.NewGuid();
        var sessions = new SessionFake(new VerificationSession { VerificationSessionId = id }) { Matches = new[] { new DimensionMatch { DimensionMatchId = matchId, VerificationSessionId = id, Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra" } } };
        var auth = new SessionAuthenticationService(); var service = new DimensionMatchingService(new DocumentsFake(), sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration());
        auth.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" })); Assert.Single(await service.GetExtraResultsAsync(id)); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddExtraCommentAsync(id, matchId, "not allowed"));
        auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        await service.AddExtraCommentAsync(id, matchId, "Should be shown on drawing"); await service.FlagExtraForDesignUpdateAsync(id, matchId, true);
        Assert.True(sessions.LastReview!.RequiresDesignUpdate); Assert.Equal("Should be shown on drawing", sessions.LastReview.Comment);
    }
    [Fact(DisplayName = "TD-APP-004 replaces stale deltas after recomparison")]
    public async Task Recomparison_replaces_previous_match_values_and_delta()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var docs = new DocumentsFake(step, dxf, new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10) });
        var sessions = new SessionFake(session); var geometry = new GeometryFake(new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 9.85 });
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var service = new DimensionMatchingService(docs, sessions, geometry, auth, new DimensionMatchingConfiguration());
        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);
        Assert.Equal(.15, Assert.Single(sessions.Matches).SignedDelta!.Value, 12);
        geometry.Features = new[] { new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 10.275 } };
        await service.UpdateToleranceAndRecompareAsync(session.VerificationSessionId, .1);
        var replacement = Assert.Single(sessions.Matches); Assert.Equal(-.275, replacement.SignedDelta!.Value, 12); Assert.DoesNotContain(sessions.Matches, x => x.SignedDelta is double delta && Math.Abs(delta - .15) < 1e-12); Assert.Equal(DocumentAuditAction.ToleranceComparisonApplied, sessions.Action);
    }
    [Fact(DisplayName = "TD-APP-002 denies matching before session or geometry access")]
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

    [Fact(DisplayName = "TD-APP-002 persists authorized matching results and the matching audit action")]
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
        Assert.Equal(DocumentAuditAction.ToleranceComparisonApplied, sessions.Action);
        Assert.Single(sessions.Matches);
        Assert.Equal(DimensionMatchStatus.Matched, sessions.Matches[0].Status);
        using (var details = JsonDocument.Parse(sessions.DetailsJson!))
        {
            Assert.Equal(1, details.RootElement.GetProperty("comparedDimensions").GetInt32());
            Assert.Equal(1, details.RootElement.GetProperty("matchedDimensions").GetInt32());
            Assert.Equal(0, details.RootElement.GetProperty("mismatchedDimensions").GetInt32());
        }
        Assert.Equal(1, geometry.CallCount);
    }

    [Fact(DisplayName = "TD-APP-002 persists unmatched reasons, metadata, count, and audit breakdown")]
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

        Assert.Equal(DocumentAuditAction.ToleranceComparisonApplied, sessions.Action);
        var unmatched = Assert.Single(sessions.Matches, x => x.Status == DimensionMatchStatus.Unmatched);
        Assert.Equal("NoCorrespondingGeometryFound", unmatched.ReasonCode);
        Assert.Equal(DimensionType.Linear, unmatched.DimensionType);
        Assert.Equal(99, unmatched.NominalValue);
        Assert.Equal("99 mm", unmatched.DimensionText);
        using var details = JsonDocument.Parse(sessions.DetailsJson!);
        Assert.Equal(session.VerificationSessionId, details.RootElement.GetProperty("verificationSessionId").GetGuid());
        Assert.Equal("checker-id", sessions.UserId);
        Assert.Equal(2, details.RootElement.GetProperty("comparedDimensions").GetInt32());
        Assert.Equal(1, details.RootElement.GetProperty("unmatchedDimensions").GetInt32());
        Assert.Equal(1, details.RootElement.GetProperty("reasonBreakdown").GetProperty("NoCorrespondingGeometryFound").GetInt32());
    }

    [Fact(DisplayName = "TD-02 records DXF-unmatched and STEP-extra counts in the audit payload")]
    public async Task Matching_audit_distinguishes_dxf_unmatched_from_extra_step_features()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var dimensions = new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 10) };
        var sessions = new SessionFake(session); var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }) { UserId = "maker" });
        var features = new[] { new GeometryFeatureData { Reference = "Edge:matched", GeometryType = "Line", Length = 10 }, new GeometryFeatureData { Reference = "Edge:extra", GeometryType = "Line", Length = 99 } };
        await new DimensionMatchingService(new DocumentsFake(step, dxf, dimensions), sessions, new GeometryFake(features), auth, new DimensionMatchingConfiguration()).MatchDimensionsToGeometryAsync(session.VerificationSessionId);
        using var details = JsonDocument.Parse(sessions.DetailsJson!);
        Assert.Equal(0, details.RootElement.GetProperty("missingDxfCount").GetInt32()); Assert.Equal(1, details.RootElement.GetProperty("extraStepCount").GetInt32()); Assert.Equal(1, sessions.Matches.Count(match => match.Status == DimensionMatchStatus.ExtraDimension));
    }

    [Fact(DisplayName = "TD-03 enforces Maker comment and resolution role boundaries")]
    public async Task Comment_and_resolution_operations_follow_role_matrix()
    {
        var matchId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var sessions = new SessionFake(new VerificationSession { VerificationSessionId = sessionId })
        {
            Matches = new[] { new DimensionMatch { DimensionMatchId = matchId, VerificationSessionId = sessionId, Status = DimensionMatchStatus.Unmatched } }
        };
        var auth = new SessionAuthenticationService(); var service = new DimensionMatchingService(new DocumentsFake(), sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration());
        auth.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }));
        await service.AddCommentAsync(sessionId, matchId, "Expected missing");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveMissingDimensionAsync(sessionId, matchId, DimensionResolutionStatus.Accepted));
        Assert.Equal("Expected missing", sessions.LastReview!.Comment);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCommentAsync(sessionId, matchId, " "));
        auth.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddCommentAsync(sessionId, matchId, "not allowed"));
    }

    [Fact(DisplayName = "TD-APP-003 authorizes result retrieval before filtering or repository access")]
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

    [Fact(DisplayName = "TD-APP-002 supports a 500-dimension bounded comparison")]
    public async Task Five_hundred_dimensions_complete_through_the_matching_service()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var dimensions = Enumerable.Range(1, 500).Select(i => new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, i, DimensionType.Linear, i)).ToArray();
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var sessions = new SessionFake(session);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var features = Enumerable.Range(1, 500).Select(i => new GeometryFeatureData { Reference = $"Edge:{i}", GeometryType = "Line", Length = i }).ToArray();
        var service = new DimensionMatchingService(new DocumentsFake(step, dxf, dimensions), sessions, new GeometryFake(features), auth, new DimensionMatchingConfiguration());
        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);
        Assert.Equal(500, sessions.Matches.Count(x => x.DxfDimensionReference > 0));
        using var details = JsonDocument.Parse(sessions.DetailsJson!);
        Assert.Equal(500, details.RootElement.GetProperty("comparedDimensions").GetInt32());
    }

    [Fact(DisplayName = "TD-APP-002 denies tolerance management to unauthenticated and Viewer users")]
    public async Task Tolerance_update_requires_maker_or_checker()
    {
        var sessions = new SessionFake();
        var auth = new SessionAuthenticationService();
        var service = new DimensionMatchingService(new DocumentsFake(), sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateToleranceAndRecompareAsync(Guid.NewGuid(), .2));
        auth.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateToleranceAndRecompareAsync(Guid.NewGuid(), .2));
        Assert.Equal(0, sessions.ReplaceCount);
    }

    [Fact(DisplayName = "TD-APP-003 denies GD&T review mutations before repository access")]
    public async Task Gdt_review_mutation_requires_modify_permission()
    {
        var sessions = new SessionFake();
        var auth = new SessionAuthenticationService();
        var service = new DimensionMatchingService(new DocumentsFake(), sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration());

        auth.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateGdtReviewAsync(Guid.NewGuid(), Guid.NewGuid(), "not allowed", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateGdtDispositionAsync(Guid.NewGuid(), Guid.NewGuid(), "Future scope"));
        Assert.Equal(0, sessions.MatchLookupCount);
    }

    [Fact(DisplayName = "TD-APP-002 propagates cancellation as a failed comparison")]
    public async Task Cancellation_is_not_reported_as_success()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }));
        var service = new DimensionMatchingService(new DocumentsFake(step, dxf, new[] { new CoreDimensionData(dxf.DocumentId, dxf.Version!.VersionId, 1, DimensionType.Linear, 1) }), new SessionFake(new VerificationSession { StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId }), new GeometryFake(new GeometryFeatureData { Reference = "edge", Length = 1 }), auth, new DimensionMatchingConfiguration());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MatchDimensionsToGeometryAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact(DisplayName = "TD-KAN45-REVIEW-02 persists structured DXF extraction failures with source, audit, and logging context")]
    public async Task Matching_orchestration_persists_structured_dxf_failure()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var sessions = new SessionFake(session);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var logger = new RecordingLogger();
        var service = CreateGdtService(new DocumentsFake(step, dxf), sessions, auth, logger, DxfGdtExtractionResult.Failure("WorkerProtocol", "DXF worker protocol failed."));

        await service.MatchDimensionsToGeometryAsync(session.VerificationSessionId);

        var result = Assert.Single(sessions.GdtResults);
        Assert.Equal(VerificationStatus.CannotValidate, result.Status);
        Assert.Equal(GdtResult.ParsingReason, result.Reason);
        Assert.StartsWith("DXF:", result.SourceDocument, StringComparison.Ordinal);
        Assert.Contains("Extraction category: WorkerProtocol", result.Diagnostics);
        Assert.Equal(DocumentAuditAction.ToleranceComparisonApplied, sessions.Action);
        Assert.Contains(logger.Operations, operation => operation.Contains("DXF GD&T extraction", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "TD-KAN45-REVIEW-02 maps parser exceptions and STEP failures through the matching orchestration")]
    public async Task Matching_orchestration_maps_parser_and_step_failures()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });

        var parserSession = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var parserSessions = new SessionFake(parserSession); var parserLogger = new RecordingLogger();
        var parserService = new DimensionMatchingService(new DocumentsFake(step, dxf), parserSessions, new GeometryFake(), auth, new DimensionMatchingConfiguration(), logger: parserLogger,
            gdtVerification: new VerificationService(new VerificationEngine(new GdtValidator()), parserSessions), dxfFiles: new DxfStoreFake(), dxfGdtParser: new ThrowingParser(), stepGdtService: new StepFake(GdtAnnotationExtractionResult.Unsupported("unreachable")));
        await parserService.MatchDimensionsToGeometryAsync(parserSession.VerificationSessionId);
        Assert.Equal("Parser", Assert.Single(parserSessions.GdtResults).Diagnostics.Single(d => d.StartsWith("Extraction category:", StringComparison.Ordinal)).Replace("Extraction category: ", string.Empty));
        Assert.Contains(parserLogger.Operations, operation => operation == "DXF GD&T extraction");

        var stepSession = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var stepSessions = new SessionFake(stepSession); var stepLogger = new RecordingLogger();
        var stepService = new DimensionMatchingService(new DocumentsFake(step, dxf), stepSessions, new GeometryFake(), auth, new DimensionMatchingConfiguration(), logger: stepLogger,
            gdtVerification: new VerificationService(new VerificationEngine(new GdtValidator()), stepSessions), dxfFiles: new DxfStoreFake(), dxfGdtParser: new ParserFake(DxfGdtExtractionResult.Success(Array.Empty<GdtAnnotationData>())), stepGdtService: new StepFake(GdtAnnotationExtractionResult.Failure("WorkerProtocol", "STEP worker protocol failed.")));
        await stepService.MatchDimensionsToGeometryAsync(stepSession.VerificationSessionId);
        var stepResult = Assert.Single(stepSessions.GdtResults);
        Assert.StartsWith("STEP:", stepResult.SourceDocument, StringComparison.Ordinal);
        Assert.Contains("Extraction category: WorkerProtocol", stepResult.Diagnostics);
        Assert.Contains(stepLogger.Operations, operation => operation == "STEP GD&T extraction");
    }

    [Fact(DisplayName = "TD-KAN45-REVIEW-02 propagates cancellation from the injected extraction path")]
    public async Task Matching_orchestration_does_not_persist_on_caller_cancellation()
    {
        var step = NewDocument(CoreDocumentType.StepModel, CoreGeometryStatus.Processed, VerificationStatus.Unknown);
        var dxf = NewDocument(CoreDocumentType.DxfDrawing, CoreGeometryStatus.NotExtracted, VerificationStatus.DimensionsExtracted);
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentId = step.DocumentId, DxfDocumentId = dxf.DocumentId };
        var sessions = new SessionFake(session);
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var cancellation = new CancellationTokenSource();
        var service = CreateGdtService(new DocumentsFake(step, dxf), sessions, auth, new RecordingLogger(), cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MatchDimensionsToGeometryAsync(session.VerificationSessionId, cancellation.Token));
        Assert.Empty(sessions.GdtResults);
    }

    private static DimensionMatchingService CreateGdtService(DocumentsFake documents, SessionFake sessions, SessionAuthenticationService auth, RecordingLogger logger, DxfGdtExtractionResult result)
        => new(documents, sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration(), logger: logger,
            gdtVerification: new VerificationService(new VerificationEngine(new GdtValidator()), sessions),
            dxfFiles: new DxfStoreFake(), dxfGdtParser: new ParserFake(result), stepGdtService: new StepFake(GdtAnnotationExtractionResult.Unsupported("STEP capability unavailable.")));

    private static DimensionMatchingService CreateGdtService(DocumentsFake documents, SessionFake sessions, SessionAuthenticationService auth, RecordingLogger logger, CancellationTokenSource cancellation)
        => new(documents, sessions, new GeometryFake(), auth, new DimensionMatchingConfiguration(), logger: logger,
            gdtVerification: new VerificationService(new VerificationEngine(new GdtValidator()), sessions),
            dxfFiles: new DxfStoreFake(cancellation), dxfGdtParser: new ParserFake(DxfGdtExtractionResult.Success(Array.Empty<GdtAnnotationData>())), stepGdtService: new StepFake(GdtAnnotationExtractionResult.Unsupported("STEP capability unavailable.")));

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
        public DimensionReview? LastReview { get; private set; }
        private readonly VerificationSession _session = session ?? new VerificationSession(); public int LookupCount; public int ReplaceCount; public int MatchLookupCount; public DocumentAuditAction Action; public string? UserId; public string? DetailsJson; public IReadOnlyList<DimensionMatch> Matches = Array.Empty<DimensionMatch>();
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) { LookupCount++; return Task.FromResult<VerificationSession?>(_session with { VerificationSessionId = id }); }
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken cancellationToken = default) => Task.FromResult(_session);
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) { MatchLookupCount++; return Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? Matches : Matches.Where(x => x.Status == status).ToArray()); }
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction action, string userId, string detailsJson, CancellationToken cancellationToken = default) { ReplaceCount++; Action = action; UserId = userId; DetailsJson = detailsJson; Matches = matches.ToArray(); return Task.CompletedTask; }
        public Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, CancellationToken cancellationToken = default) { LastReview = review; return Task.CompletedTask; }
        public IReadOnlyList<GdtResult> GdtResults { get; private set; } = Array.Empty<GdtResult>();
        public Task<IReadOnlyList<GdtResult>> GetGdtResultsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(GdtResults);
        public Task ReplaceGdtResultsAsync(Guid id, IReadOnlyCollection<GdtResult> results, string userId, CancellationToken cancellationToken = default) { GdtResults = results.ToArray(); return Task.CompletedTask; }
    }
    private sealed class GeometryFake(params GeometryFeatureData[] features) : IMatchableGeometryService
    { public int CallCount; public GeometryFeatureData[] Features { get; set; } = features; public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default) { CallCount++; return new(true, Features); } }
    private sealed class ParserFake(DxfGdtExtractionResult result) : IDxfGdtParser
    { public Task<DxfGdtExtractionResult> ParseDetailedAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default) => Task.FromResult(result); }
    private sealed class ThrowingParser : IDxfGdtParser
    { public Task<DxfGdtExtractionResult> ParseDetailedAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default) => throw new InvalidDataException("Malformed DXF response."); }
    private sealed class StepFake(GdtAnnotationExtractionResult result) : IGdtAnnotationService
    { public GdtAnnotationExtractionResult ExtractAnnotations(string filePath, CancellationToken cancellationToken = default) => result; }
    private sealed class DxfStoreFake(CancellationTokenSource? cancellation = null) : IDxfUploadFileStore
    {
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        { cancellation?.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream()); }
    }
    private sealed class RecordingLogger : ICentralizedLogger
    { public List<string> Operations { get; } = new(); public void Log(Exception exception, string operation, string context) => Operations.Add(operation); }
}
