using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditLogServiceTests
{
    [Fact(DisplayName = "TD-APP-003 audit details deserialize extra count and trigger identity")]
    public void Extra_dimension_audit_payload_has_required_fields()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { stepDocumentId = Guid.NewGuid(), dxfDocumentId = Guid.NewGuid(), extraDimensionCount = 2, timestampUtc = DateTime.UtcNow, triggeredBy = "checker" });
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("extraDimensionCount").GetInt32());
        Assert.Equal("checker", root.GetProperty("triggeredBy").GetString());
        Assert.Equal(DateTimeKind.Utc, root.GetProperty("timestampUtc").GetDateTime().Kind);
    }
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
