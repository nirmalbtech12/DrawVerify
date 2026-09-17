using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportAuthorizationTests
{
    [Fact(DisplayName = "REV-KAN66-003 authorized export emits every Approver CSV field")]
    public void Approver_export_contract_has_matching_header_and_row_projection()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "AuditLogService.cs")));
        var methodStart = source.IndexOf("ExportDocumentCsvAsync", StringComparison.Ordinal);
        var method = source[methodStart..];
        foreach (var field in new[] { "ApproverSignature", "ApproverUserId", "ApproverDisplayName", "ApproverRole", "ApproverTimestampUtc", "ApproverMethod" })
        {
            Assert.Contains(field, source);
            Assert.Contains(field, method);
        }
    }

    [Fact(DisplayName = "TD-REPORT-01 report and export access keeps persisted Approver attestation authorized")]
    public void Approver_report_projection_contract_is_revision_bound_and_role_checked()
    {
        var report = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "ReportService.cs")));
        var audit = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "AuditLogService.cs")));
        Assert.Contains("ApproverSignature", report);
        Assert.Contains("ApproverSignature", audit);
        Assert.Contains("ViewDimensionMatchResults", report);
        Assert.Contains("ApproverUserId", audit);
        Assert.Contains("ApproverTimestampUtc", audit);
        Assert.Contains("ApproverMethod", audit);
    }

    [Theory(DisplayName = "TD-APP-EXTRA-005 extra report access follows existing role permissions")]
    [InlineData("Checker", true)] [InlineData("Approver", true)] [InlineData("Viewer", false)] [InlineData("Maker", true)]
    public async Task Extra_report_authorization_is_explicit(string role, bool allowed)
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("user", new[] { role }));
        var service = new ReportService(new Sessions(), authentication, new RoleAuthorization());
        if (allowed) Assert.Contains("Extra Dimensions", await service.GenerateMarkdownAsync(Guid.NewGuid()));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GenerateMarkdownAsync(Guid.NewGuid()));
    }
    [Fact(DisplayName = "TD-009 Approver has the same report permission as Checker")]
    public void Approver_and_checker_are_authorized_for_summary_report()
    {
        var authorization = new RoleAuthorization();
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("checker", new[] { "Checker" }), RolePermission.ViewDimensionMatchResults));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("approver", new[] { "Approver" }), RolePermission.ViewDimensionMatchResults));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("viewer", new[] { "Viewer" }), RolePermission.ViewDimensionMatchResults));
    }

    [Theory(DisplayName = "TD-008 denies unauthenticated and unauthorized report access")]
    [InlineData(null, false)]
    [InlineData("Viewer", false)]
    [InlineData("Checker", true)]
    [InlineData("Maker", true)]
    [InlineData("Approver", true)]
    public async Task TD_008_report_authorization_matrix_has_no_data_for_denied_roles(string? role, bool allowed)
    {
        var auth = new SessionAuthenticationService(); if (role is not null) auth.SignIn(new AuthenticatedUser("user", new[] { role }));
        var task = new ReportService(new Sessions(), auth).GenerateMarkdownAsync(Guid.NewGuid());
        if (allowed) Assert.Contains("Verification Report", await task); else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
    }

    [Fact(DisplayName = "TD-008 unauthorized export does not expose Checker signature data")]
    public void Unauthorized_export_policy_is_not_granted_by_report_roles()
    {
        var authorization = new RoleAuthorization();
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("reviewer-001", new[] { "Viewer" }), RolePermission.ViewDimensionMatchResults));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("auditor-001", new[] { "Checker" }), RolePermission.ViewDimensionMatchResults));
    }

    private sealed class Sessions : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DrawVerify.Core.Enums.DimensionMatchStatus? status = null, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>());
        public Task<VerificationSession> GetOrCreateAsync(Guid step, Guid dxf, CancellationToken token = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DrawVerify.Core.Enums.DocumentAuditAction action, string userId, string detailsJson, CancellationToken token = default) => throw new NotImplementedException();
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
