using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MissingDimensionReviewServiceTests
{
    [Theory(DisplayName = "TD-APP-EXTRA-005 extra review access is enforced at the review service boundary")]
    [InlineData(SystemRoles.Checker, true)] [InlineData(SystemRoles.Approver, true)] [InlineData(SystemRoles.Viewer, true)] [InlineData(SystemRoles.Maker, true)] [InlineData("Unknown", false)]
    public async Task Extra_review_query_authorization_matches_role_policy(string role, bool allowed)
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("user", new[] { role }));
        var service = new MissingDimensionReviewService(new ReviewRepository(Guid.NewGuid(), Array.Empty<DimensionMatch>()), authentication);
        if (allowed) Assert.Empty(await service.QueryExtraAsync(Guid.NewGuid())); else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueryExtraAsync(Guid.NewGuid()));
    }
    [Fact(DisplayName = "TD-APP-EXTRA-004 queries persisted extra dimensions")]
    public async Task Query_extra_returns_only_extra_rows_and_metadata()
    {
        var sessionId = Guid.NewGuid();
        var repository = new ReviewRepository(sessionId, new[]
        {
            Match(1, "A", "Block", "Front", 1, 2) with { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra", DxfDimensionReference = -1 },
            Match(2, "A", "Block", "Front", 3, 4)
        });
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }));
        var service = new MissingDimensionReviewService(repository, authentication);
        var results = await service.QueryExtraAsync(sessionId);
        var extra = Assert.Single(results); Assert.Equal("Edge:extra", extra.StepGeometryReference); Assert.Equal(DimensionMatchStatus.ExtraDimension, extra.Status);
    }
    [Fact(DisplayName = "TD-004 queries missing dimensions by location and summarizes them")]
    public async Task Query_filters_sorts_and_summarizes_location_metadata()
    {
        var sessionId = Guid.NewGuid();
        var repository = new ReviewRepository(sessionId, new[]
        {
            Match(3, "B", "Block-2", "Back", 30, 2), Match(1, "A", "Block-1", "Front", 10, 9), Match(2, "A", "Block-1", "Front", 20, 8)
        });
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker" });
        var service = new MissingDimensionReviewService(repository, authentication);

        var byX = await service.QueryAsync(sessionId, new MissingDimensionQuery(SortBy: MissingDimensionSortField.X));
        Assert.Equal(new[] { 1, 2, 3 }, byX.Select(item => item.DxfDimensionReference));
        var filtered = await service.QueryAsync(sessionId, new MissingDimensionQuery(Layer: "a", View: "front", SortBy: MissingDimensionSortField.Y, Descending: true));
        Assert.Equal(new[] { 1, 2 }, filtered.Select(item => item.DxfDimensionReference));
        var summary = await service.GetSummaryAsync(sessionId);
        Assert.Equal(3, summary.Total); Assert.Equal(2, summary.ByLayer["A"]); Assert.Equal(2, summary.ByView["Front"]);
    }

    [Theory(DisplayName = "TD-004 missing-dimension access follows the configured role catalog")]
    [InlineData(SystemRoles.Checker, true)] [InlineData(SystemRoles.Approver, true)] [InlineData(SystemRoles.Viewer, true)] [InlineData(SystemRoles.Maker, true)] [InlineData("Unknown", false)]
    public async Task Query_authorization_is_explicit(string role, bool allowed)
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser(role, new[] { role }));
        var service = new MissingDimensionReviewService(new ReviewRepository(Guid.NewGuid(), Array.Empty<DimensionMatch>()), authentication);
        if (allowed) Assert.Empty(await service.QueryAsync(Guid.NewGuid())); else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueryAsync(Guid.NewGuid()));
    }

    private static DimensionMatch Match(int reference, string layer, string block, string view, double x, double y) => new()
    {
        DimensionMatchId = Guid.NewGuid(), DxfDimensionReference = reference, Status = DimensionMatchStatus.Unmatched, NominalValue = 50, DimensionText = "50.00", DxfLayer = layer, BlockName = block, ViewName = view, LocationX = x, LocationY = y
    };

    private sealed class ReviewRepository(Guid id, IReadOnlyList<DimensionMatch> matches) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = sessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? matches : matches.Where(item => item.Status == status).ToArray());
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => Task.FromResult(new VerificationSession { VerificationSessionId = id });
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> values, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
