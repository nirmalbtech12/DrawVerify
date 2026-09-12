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
        Assert.Contains(vm.Users, user => user.Username == "CREATED.USER");
        Assert.Equal(string.Empty, vm.Username);
        Assert.Equal(string.Empty, vm.DisplayName);
        Assert.Equal(string.Empty, vm.InitialPassword);
        var countBeforeCreateAnother = vm.Users.Count;
        vm.CreateAnotherCommand.Execute(null);
        Assert.Equal(countBeforeCreateAnother, vm.Users.Count);
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact(DisplayName = "TD-UI-001 displays the three workflow roles, preselects, and prevents duplicate selection")]
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
        Assert.Equal(new[] { "Maker", "Checker", "Approver" }, vm.RoleOptions.Select(x => x.Id));
        Assert.Equal(new[] { "Maker" }, vm.AssignedRoles);
        Assert.Equal(new[] { "Maker" }, vm.CurrentRoles);
        Assert.False(vm.IsRoleDirty);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Maker").IsSelected);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Maker").IsCurrent);

        vm.ToggleRole("Checker");
        vm.ToggleRole("Checker");
        Assert.DoesNotContain("Checker", vm.AssignedRoles);
        vm.ToggleRole("Checker");
        Assert.True(vm.IsRoleDirty);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Checker").IsPending);
        await vm.SetRolesAsync();

        Assert.Equal("Roles updated for 'MAKER'.", vm.Message);
        Assert.False(vm.IsRoleDirty);
        Assert.Equal(new[] { "Checker", "Maker" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
        vm.SelectedUser = vm.Users.Single(x => x.Username == "CHECKER");
        Assert.Equal("CHECKER", vm.SelectedUsername);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Checker").IsSelected);
    }

    [Fact(DisplayName = "TD-KAN-120-01 workflow-role draft state and navigation preserve or discard changes explicitly")]
    public async Task Role_draft_state_and_navigation_follow_discard_contract()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = await LoginAsAdminAsync(seed);
        var vm = CreateViewModel(seed, session);
        await vm.LoadUsersAsync();
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        Assert.True(vm.RoleBackCommand.CanExecute(null));
        Assert.True(vm.RoleCancelCommand.CanExecute(null));
        Assert.True(vm.SetRolesCommand.CanExecute(null));

        Assert.Equal(new[] { "Maker" }, vm.CurrentRoles);
        Assert.Equal(new[] { "Maker" }, vm.AssignedRoles);
        Assert.False(vm.IsRoleDirty);
        Assert.Equal("Current role", vm.RoleOptions.Single(x => x.Id == "Maker").StateLabel);

        var navigationRequests = 0;
        var declined = true;
        void HandleRoleExit()
        {
            navigationRequests++;
            if (!declined) vm.DiscardRoles();
        }
        vm.RoleBackRequested += HandleRoleExit;
        vm.RoleCancelRequested += HandleRoleExit;
        vm.RoleBackCommand.Execute(null);
        Assert.Equal(1, navigationRequests);
        Assert.False(vm.IsRoleDirty);

        vm.ToggleRole("Maker");
        vm.ToggleRole(SystemRoles.Approver);
        Assert.True(vm.IsRoleDirty);
        Assert.True(vm.RoleOptions.Single(x => x.Id == "Maker").IsPending);
        Assert.True(vm.RoleOptions.Single(x => x.Id == SystemRoles.Approver).IsPending);
        Assert.Contains("pending", vm.RoleOptions.Single(x => x.Id == SystemRoles.Approver).StateLabel, StringComparison.OrdinalIgnoreCase);

        vm.RoleCancelCommand.Execute(null);
        Assert.True(vm.IsRoleDirty);
        Assert.Contains(SystemRoles.Approver, vm.AssignedRoles);

        declined = false;
        vm.RoleBackCommand.Execute(null);
        Assert.False(vm.IsRoleDirty);
        Assert.Equal(new[] { "Maker" }, vm.AssignedRoles);
        Assert.False(vm.RoleOptions.Single(x => x.Id == SystemRoles.Approver).IsPending);
    }

    [Fact(DisplayName = "TD-UI-002 consequential role removals require confirmation before persistence")]
    public async Task Consequential_role_removals_request_confirmation_and_decline_without_saving()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = await LoginAsAdminAsync(seed);
        var vm = CreateViewModel(seed, session);
        await vm.LoadUsersAsync();

        var prompts = new List<string>();
        vm.RoleConfirmation = prompt => { prompts.Add(prompt); return false; };
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        vm.ToggleRole("Maker");
        await vm.SetRolesAsync();
        Assert.Single(prompts);
        Assert.Contains("no roles", prompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Maker" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
        Assert.Empty(vm.AssignedRoles);
        Assert.True(vm.IsRoleDirty);

        vm.RoleConfirmation = _ => true;
        await vm.SetRolesAsync();
        Assert.Equal("Roles updated for 'MAKER'.", vm.Message);
        Assert.Empty((await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);

        prompts.Clear();
        vm.RoleConfirmation = prompt => { prompts.Add(prompt); return false; };
        vm.SelectedUser = vm.Users.Single(x => x.Username == "APPROVER");
        vm.ToggleRole(SystemRoles.Approver);
        await vm.SetRolesAsync();
        Assert.Single(prompts);
        Assert.Contains("Approver", prompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Approver" }, (await seed.Repository.FindByUsernameAsync("APPROVER"))!.Roles);
        Assert.Empty(vm.AssignedRoles);
        Assert.True(vm.IsRoleDirty);
    }

    [Fact(DisplayName = "TD-UI-004 denies non-Admin editing while displaying own current role")]
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

    [Fact(DisplayName = "TD-UI-003 reloads authoritative roles after a stale save")]
    public async Task Stale_save_preserves_draft_and_retry_uses_refreshed_version()
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

        vm.ToggleRole(SystemRoles.Checker);
        await vm.SetRolesAsync();

        Assert.Contains("changed", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { SystemRoles.Maker, SystemRoles.Checker }, vm.AssignedRoles);
        Assert.True(vm.IsRoleDirty);
        Assert.True(vm.RoleOptions.Single(x => x.Id == SystemRoles.Maker).IsPending);
        Assert.False(vm.RoleOptions.Single(x => x.Id == SystemRoles.Checker).IsPending);

        await vm.SetRolesAsync();

        Assert.Equal("Roles updated for 'MAKER'.", vm.Message);
        Assert.False(vm.IsRoleDirty);
        Assert.Equal(new[] { SystemRoles.Checker, SystemRoles.Maker }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
    }

    [Fact(DisplayName = "TD-UI-002 Save and Cancel expose success, pending, and no-write behavior")]
    public async Task Role_editor_cancel_restores_authoritative_state_and_save_confirms_success()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = await LoginAsAdminAsync(seed);
        var vm = CreateViewModel(seed, session);
        await vm.LoadUsersAsync();
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        vm.ToggleRole(SystemRoles.Checker);
        Assert.True(vm.IsRoleDirty);
        Assert.Contains("pending", vm.RoleOptions.Single(x => x.Id == SystemRoles.Checker).StateLabel, StringComparison.OrdinalIgnoreCase);
        vm.DiscardRoles();
        Assert.False(vm.IsRoleDirty);
        Assert.Equal(new[] { SystemRoles.Maker }, vm.AssignedRoles);
        vm.ToggleRole(SystemRoles.Checker);
        await vm.SetRolesAsync();
        Assert.Equal("Roles updated for 'MAKER'.", vm.Message);
        Assert.False(vm.IsRoleDirty);
    }

    [Fact(DisplayName = "TD-UI-003 validation failure refreshes authoritative role state")]
    public async Task Validation_failure_does_not_present_local_roles_as_saved()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var session = await LoginAsAdminAsync(seed);
        var vm = CreateViewModel(seed, session);
        await vm.LoadUsersAsync();
        vm.SelectedUser = vm.Users.Single(x => x.Username == "MAKER");
        vm.ToggleRole(SystemRoles.Checker);
        var current = await seed.Repository.FindByUsernameAsync("MAKER");
        Assert.True((await seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Checker }, current!.RoleVersion,
            new("ADMIN", "MAKER", current.Roles, new[] { SystemRoles.Checker }, DateTimeOffset.UtcNow, "RolesUpdated"))).Succeeded);
        await vm.SetRolesAsync();
        Assert.Contains("changed", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { SystemRoles.Checker }, vm.CurrentRoles);
        Assert.Equal(new[] { SystemRoles.Maker, SystemRoles.Checker }, vm.AssignedRoles);
        Assert.True(vm.IsRoleDirty);
    }

    private static async Task<SessionAuthenticationService> LoginAsAdminAsync(AuthenticationAuthorizationTestSeed seed)
    {
        var session = new SessionAuthenticationService();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        return session;
    }

    private static UserAdministrationViewModel CreateViewModel(AuthenticationAuthorizationTestSeed seed, SessionAuthenticationService session)
    {
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        return new UserAdministrationViewModel(new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake()));
    }

    private sealed class LoggerFake : ICentralizedLogger
    {
        public void Log(Exception exception, string operation, string context) { }
    }
}
