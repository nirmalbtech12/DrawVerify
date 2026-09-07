using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Tests;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class UserAdministrationViewModelTests
{
    [Fact(DisplayName = "TD-WPF-ROLE-003 displays the five roles, preselects, saves, and prevents duplicate selection")]
    public async Task Admin_role_editor_uses_persisted_seed_and_confirms_success()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()));

        await vm.LoadUsersAsync();
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        Assert.Equal(new[] { "Admin", "Maker", "Checker", "Approver", "Viewer" }, vm.RoleOptions.Select(x => x.Id));
        Assert.Equal(new[] { "Maker" }, vm.AssignedRoles);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Maker").IsSelected);

        vm.ToggleRole("Checker");
        vm.ToggleRole("Checker");
        Assert.DoesNotContain("Checker", vm.AssignedRoles);
        vm.ToggleRole("Checker");
        await vm.SetRolesAsync();

        Assert.Contains("success", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Checker", "Maker" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
    }

    [Fact(DisplayName = "TD-WPF-ROLE-003 denies non-Admin editing while displaying own current role")]
    public async Task Non_admin_seed_user_is_read_only()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("maker", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()));

        Assert.Equal(new[] { "Maker" }, vm.AssignedRoles);
        Assert.False(vm.CanEditRoles);
        Assert.False(vm.SetRolesCommand.CanExecute(null));
        await vm.SetRolesAsync();
        Assert.Contains("administrator", vm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-WPF-ROLE-003 reloads authoritative roles after a stale save")]
    public async Task Stale_save_refreshes_selected_user_state()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()));
        await vm.LoadUsersAsync();
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        var current = await seed.Repository.FindByUsernameAsync("MAKER");
        Assert.NotNull(current);
        Assert.True((await seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Checker }, current!.RoleVersion,
            new("ADMIN", "MAKER", current.Roles, new[] { SystemRoles.Checker }, DateTimeOffset.UtcNow, "RolesUpdated"))).Succeeded);

        vm.ToggleRole(SystemRoles.Viewer);
        await vm.SetRolesAsync();

        Assert.Contains("changed", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { SystemRoles.Checker }, vm.AssignedRoles);
    }

    private sealed class LoggerFake : ICentralizedLogger
    {
        public void Log(Exception exception, string operation, string context) { }
    }
}
