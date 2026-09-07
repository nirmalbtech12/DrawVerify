using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Tests;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact(DisplayName = "TD-01 audits creation, profile changes, deactivation, and role classifications")]
    public async Task Lifecycle_and_role_operations_write_structured_non_secret_audit_rows()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var session = new SessionAuthenticationService();
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var service = new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration { DefaultRoles = new[] { "Viewer" } }, new LoggerFake());

        Assert.True((await service.CreateUserAsync(new("new-user", "New User", "GoodPassword1!"))).Succeeded);
        var created = (await audit.QueryAsync(new AuditLogQuery(PageSize: 100))).Entries.Single(x => x.Action == "UserCreated" && x.Username == "NEW-USER");
        Assert.Equal("ADMIN", created.ActorUsername);
        Assert.Equal("[\"Viewer\"]", created.RolesAfter);

        Assert.True((await service.UpdateProfileAsync("NEW-USER", "Renamed", "new@example.test", true)).Succeeded);
        var modified = (await audit.QueryAsync(new AuditLogQuery(PageSize: 100))).Entries.Single(x => x.Action == "UserModified");
        Assert.Contains("DisplayName", modified.ChangedFields);
        Assert.Contains("Email", modified.ChangedFields);
        Assert.Contains("PasswordReset", modified.ChangedFields);
        Assert.DoesNotContain("GoodPassword1!", string.Join('|', modified.OldValues, modified.NewValues));

        Assert.True((await service.DeactivateUserAsync("NEW-USER", "offboarding")).Succeeded);
        var deactivated = (await audit.QueryAsync(new AuditLogQuery(PageSize: 100))).Entries.Single(x => x.Action == "UserDeactivated");
        Assert.Equal("offboarding", deactivated.DeactivationReason);
        Assert.False((await seed.Repository.FindByUsernameAsync("NEW-USER"))!.IsActive);
    }

    [Fact(DisplayName = "TD-02 and TD-07 enforce role audit authorization and awaited persistence")]
    public async Task Seeded_accounts_cover_authorization_assignment_and_session_freshness()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var session = new SessionAuthenticationService();
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var service = new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());

        var users = await service.ListUsersAsync();
        Assert.Equal(5, users.Count);
        var maker = users.Single(x => x.Username == "MAKER");
        var result = await service.SetUserRolesAsync("maker", new[] { "Checker", "Viewer" }, maker.RoleVersion);
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "Checker", "Viewer" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);

        var stale = await service.SetUserRolesAsync("MAKER", new[] { "Maker" }, maker.RoleVersion);
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.Message!, StringComparison.OrdinalIgnoreCase);

        var self = await seed.Repository.FindByUsernameAsync("ADMIN");
        var selfResult = await service.SetUserRolesAsync("ADMIN", new[] { SystemRoles.Admin, SystemRoles.Viewer }, self!.RoleVersion);
        Assert.True(selfResult.Succeeded);
        Assert.Equal(new[] { SystemRoles.Admin, SystemRoles.Viewer }, session.CurrentUser!.Roles);

        session.SignOut();
        Assert.True((await login.LoginAsync("maker", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var nonAdmin = new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        var denied = await nonAdmin.SetUserRolesAsync("CHECKER", new[] { SystemRoles.Viewer });
        Assert.False(denied.Succeeded);
        Assert.Contains("administrator", denied.Message!, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => nonAdmin.ListUsersAsync());
    }

    private sealed class LoggerFake : ICentralizedLogger
    {
        public void Log(Exception exception, string operation, string context) { }
    }
}
