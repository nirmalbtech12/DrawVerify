using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

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
    private sealed class UsersFake : IUserRepository { public List<UserAccount> Accounts{get;}=[]; public Task<UserAccount?> FindByUsernameAsync(string n,CancellationToken t=default)=>Task.FromResult<UserAccount?>(null); public Task CreateAsync(UserAccount a,AuthenticationAuditEntry e,CancellationToken t=default){Accounts.Add(a);return Task.CompletedTask;} public Task<LoginAttemptState> RecordLoginAttemptAsync(string n,bool s,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(null,false,null)); }
    private sealed class AuditFake : IAuthenticationAuditRepository { public Task WriteAsync(AuthenticationAuditEntry e,CancellationToken t=default)=>Task.CompletedTask; }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception e,string o,string c){} }
}
