using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditLogServiceTests
{
    [Fact(DisplayName = "TD-05 queries and exports filtered audit data for Admin only")]
    public async Task Admin_query_is_ordered_and_csv_escapes_values()
    {
        var repository = new AuditRepositoryFake();
        repository.Page = new AuditLogPage(new[] { new AuthenticationAuditEntry("ADMIN", "USER,1", DateTimeOffset.UnixEpoch, "UserCreated", "Success", RolesAfter: "[\"Viewer\"]") }, 1, 100, 1);
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }, 0));
        var service = new AuditLogService(repository, session);
        var page = await service.QueryAsync(new AuditLogQuery(Action: "UserCreated", Username: "USER,1"));
        Assert.Single(page.Entries);
        Assert.Equal(new[] { "UserCreated", "USER,1" }, repository.LastQuery is { } query ? new[] { query.Action!, query.Username! } : Array.Empty<string>());
        var csv = await service.ExportCsvAsync(new AuditLogQuery(Action: "UserCreated"));
        Assert.Contains("\"USER,1\"", csv);
        Assert.Contains("RolesBefore", csv);
    }

    [Fact(DisplayName = "TD-05 rejects non-Admin query and export before repository access")]
    public async Task Non_admin_cannot_query_or_export()
    {
        var repository = new AuditRepositoryFake();
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("MAKER", new[] { "Maker" }, 0));
        var service = new AuditLogService(repository, session);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueryAsync(new AuditLogQuery()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExportCsvAsync(new AuditLogQuery()));
        Assert.Null(repository.LastQuery);
    }

    private sealed class AuditRepositoryFake : IAuthenticationAuditRepository
    {
        public AuditLogPage? Page { get; set; }
        public AuditLogQuery? LastQuery { get; private set; }
        public Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { LastQuery = query; return Task.FromResult(Page ?? new AuditLogPage(Array.Empty<AuthenticationAuditEntry>(), query.Page, query.PageSize, 0)); }
    }
}
