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
    [Fact(DisplayName = "T-UX-01 clean and dirty cancellation follows the discard contract")]
    public async Task Creation_cancel_is_clean_or_dirty_and_discard_clears_state()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        Assert.True((await new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()).LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()));

        Assert.False(vm.IsCreationDirty);
        var cancelRequests = 0;
        var backRequests = 0;
        vm.CancelRequested += () => cancelRequests++;
        vm.BackRequested += () => backRequests++;
        vm.Username = "pending.user";
        vm.DisplayName = "Pending User";
        vm.InitialPassword = "GoodPassword1!";
        Assert.True(vm.IsCreationDirty);
        vm.CancelCommand.Execute(null);
        Assert.Equal(1, cancelRequests);
        vm.BackCommand.Execute(null);
        Assert.Equal(1, backRequests);

        vm.DiscardCreation();
        Assert.False(vm.IsCreationDirty);
        Assert.Equal(string.Empty, vm.Username);
        Assert.Equal(string.Empty, vm.DisplayName);
        Assert.Equal(string.Empty, vm.InitialPassword);
    }

    [Fact(DisplayName = "T-UX-02 validation maps errors while retaining entered values")]
    public async Task Creation_validation_preserves_values_and_maps_only_invalid_fields()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        Assert.True((await new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()).LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()))
        {
            Username = "valid.user",
            DisplayName = "Valid User",
            InitialPassword = "too-short"
        };

        await vm.CreateAsync();

        Assert.Equal("valid.user", vm.Username);
        Assert.Equal("Valid User", vm.DisplayName);
        Assert.True(string.Equals("too-short", vm.InitialPassword, StringComparison.Ordinal), "The entered password should be retained for correction.");
        Assert.Empty(vm.UsernameError);
        Assert.Empty(vm.DisplayNameError);
        Assert.Contains("password", vm.InitialPasswordError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("successfully", vm.Message, StringComparison.OrdinalIgnoreCase);

        vm.Username = "admin";
        vm.InitialPassword = AuthenticationAuthorizationTestSeed.Password;
        await vm.CreateAsync();

        Assert.Equal("admin", vm.Username);
        Assert.Equal("Valid User", vm.DisplayName);
        Assert.True(string.Equals(AuthenticationAuthorizationTestSeed.Password, vm.InitialPassword, StringComparison.Ordinal), "The valid password should be retained for correction.");
        Assert.Contains("already", vm.UsernameError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.DisplayNameError);
        Assert.Empty(vm.InitialPasswordError);
    }

    [Fact(DisplayName = "T-UX-03 successful creation refreshes users and supports Create Another")]
    public async Task Successful_creation_reports_normalized_user_and_resets_form()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        Assert.True((await new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()).LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        var vm = new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()))
        {
            Username = " created.user ", DisplayName = "Created User", InitialPassword = AuthenticationAuthorizationTestSeed.Password
        };

        await vm.CreateAsync();

        Assert.Equal("User 'CREATED.USER' created successfully", vm.Message);
        Assert.True(vm.Users.Any(user => user.Username == "CREATED.USER"));
        Assert.Equal(string.Empty, vm.Username);
        Assert.Equal(string.Empty, vm.DisplayName);
        Assert.Equal(string.Empty, vm.InitialPassword);
        var countBeforeCreateAnother = vm.Users.Count;
        vm.CreateAnotherCommand.Execute(null);
        Assert.Equal(countBeforeCreateAnother, vm.Users.Count);
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

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
