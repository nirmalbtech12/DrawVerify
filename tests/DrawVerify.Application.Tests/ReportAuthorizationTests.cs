using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportAuthorizationTests
{
    [Theory(DisplayName = "TD-SVC-003 denies unauthenticated and unauthorized report access")]
    [InlineData(null, false)]
    [InlineData("Viewer", false)]
    [InlineData("Checker", true)]
    [InlineData("Maker", true)]
    public async Task Existing_report_authorization_boundary_is_preserved(string? role, bool allowed)
    {
        var auth = new SessionAuthenticationService(); if (role is not null) auth.SignIn(new AuthenticatedUser("user", new[] { role }));
        var task = new ReportService(new Sessions(), auth).GenerateMarkdownAsync(Guid.NewGuid());
        if (allowed) Assert.Contains("Verification Report", await task); else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
    }

    private sealed class Sessions : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DrawVerify.Core.Enums.DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>());
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DrawVerify.Core.Enums.DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) => throw new NotImplementedException();
    }
}
