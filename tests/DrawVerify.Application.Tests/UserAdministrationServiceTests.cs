using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Tests;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact(DisplayName = "TD-APP-ROLE-002 enforces Admin-only role management and refreshes self-session roles")]
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
