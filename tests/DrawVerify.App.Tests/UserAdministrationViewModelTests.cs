using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using DrawVerify.Core.Authorization;

namespace DrawVerify.App.Tests;

public sealed class UserAdministrationViewModelTests
{
    [Fact(DisplayName = "TD-WPF-ADMIN-01 surfaces required-field and policy feedback")]
    public async Task Administration_view_model_submits_fields_and_exposes_safe_result()
    {
        var session=new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN",new[]{"Admin"})); var users=new UsersFake();
        var vm=new UserAdministrationViewModel(new UserAdministrationService(users,new AuditFake(),session,new AuthenticationConfiguration(),new LoggerFake()));
        vm.Username="new.user"; vm.DisplayName="New User"; vm.InitialPassword="GoodPassword1!"; await vm.CreateAsync();
        Assert.Equal("User created.",vm.Message); Assert.Single(users.Accounts); Assert.Empty(users.Accounts[0].Roles); Assert.Equal(string.Empty,vm.InitialPassword);
    }
    [Fact]
    public async Task Administration_view_model_clears_password_after_validation_failure()
    {
        var session=new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN",new[]{"Admin"}));
        var vm=new UserAdministrationViewModel(new UserAdministrationService(new UsersFake(),new AuditFake(),session,new AuthenticationConfiguration(),new LoggerFake())) { InitialPassword="weak" };
        await vm.CreateAsync();
        Assert.Equal(string.Empty,vm.InitialPassword); Assert.Contains("at least",vm.Message,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Administration_view_model_rejects_non_admin_without_persisting()
    {
        var session=new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("VIEWER",new[]{"Viewer"})); var users=new UsersFake();
        var vm=new UserAdministrationViewModel(new UserAdministrationService(users,new AuditFake(),session,new AuthenticationConfiguration(),new LoggerFake())) { Username="new",DisplayName="New",InitialPassword="GoodPassword1!" };
        await vm.CreateAsync(); Assert.Empty(users.Accounts); Assert.Equal(string.Empty,vm.InitialPassword);
    }

    [Fact(DisplayName = "TD-APPUI-001 Admin edits selected roles and refreshes after persistence")]
    public async Task Admin_can_edit_selected_roles_and_refresh_after_success()
    {
        var session=new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN",new[]{SystemRoles.Admin}));
        var target=Account("TARGET",SystemRoles.Viewer); var users=new UsersFake(target);
        var vm=new UserAdministrationViewModel(new UserAdministrationService(users,new AuditFake(),session,new AuthenticationConfiguration(),new LoggerFake()));
        await vm.LoadUsersAsync(); vm.SelectedUser=vm.Users.Single();
        Assert.Equal(new[]{SystemRoles.Viewer},vm.AssignedRoles);
        vm.ToggleRole(SystemRoles.Maker); await vm.SetRolesAsync();
        Assert.Equal(new[]{SystemRoles.Viewer,SystemRoles.Maker},users.Accounts.Single().Roles);
        Assert.Contains(SystemRoles.Maker,vm.AssignedRoles);
    }
    [Fact(DisplayName = "TD-APPUI-001 non-admin self profile is read-only")]
    public void Non_admin_sees_own_roles_without_editing()
    {
        var session=new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("VIEWER",new[]{SystemRoles.Viewer}));
        var vm=new UserAdministrationViewModel(new UserAdministrationService(new UsersFake(),new AuditFake(),session,new AuthenticationConfiguration(),new LoggerFake()));
        Assert.Equal(new[]{SystemRoles.Viewer},vm.AssignedRoles); Assert.False(vm.CanEditRoles); Assert.False(vm.CreateCommand.CanExecute(null)); Assert.False(vm.SetRolesCommand.CanExecute(null));
    }
    private static UserAccount Account(string username, params string[] roles) => new() { Username=username,DisplayName=username,PasswordSalt=[1],PasswordHash=[1],Roles=roles };
    private sealed class UsersFake : IUserRepository
    {
        public List<UserAccount> Accounts { get; } = [];
        public UsersFake(params UserAccount[] accounts) => Accounts.AddRange(accounts);
        public Task<UserAccount?> FindByUsernameAsync(string n,CancellationToken t=default)=>Task.FromResult(Accounts.FirstOrDefault(x=>x.Username==n));
        public Task CreateAsync(UserAccount a,AuthenticationAuditEntry e,CancellationToken t=default){Accounts.Add(a);return Task.CompletedTask;}
        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<UserAccount>>(Accounts);
        public Task ReplaceRolesAsync(string n,IReadOnlyList<string> roles,RoleAssignmentAuditEntry audit,CancellationToken t=default){var index=Accounts.FindIndex(x=>x.Username==n); var account=Accounts[index]; Accounts[index]=new UserAccount { Id=account.Id,Username=account.Username,DisplayName=account.DisplayName,PasswordSalt=account.PasswordSalt,PasswordHash=account.PasswordHash,PasswordAlgorithm=account.PasswordAlgorithm,PasswordIterations=account.PasswordIterations,Roles=roles }; return Task.CompletedTask;}
        public Task<LoginAttemptState> RecordLoginAttemptAsync(string n,bool s,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(null,false,null));
        public Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string n,Func<UserAccount,bool> verifier,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(null,false,"InvalidUsername"));
    }
    private sealed class AuditFake : IAuthenticationAuditRepository { public Task WriteAsync(AuthenticationAuditEntry e,CancellationToken t=default)=>Task.CompletedTask; }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception e,string o,string c){} }
}
