using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class LoginViewModelTests
{
    [Fact(DisplayName = "T-UX-04 failed login preserves username, clears password, and supports retry")]
    public async Task Login_view_model_handles_success_and_invalid_credentials()
    {
        var users = new UsersFake(); var session = new SessionAuthenticationService(); var audit = new AuditFake();
        var vm = new LoginViewModel(new UsernamePasswordAuthenticationService(users,audit,session,new AuthenticationConfiguration(),new LoggerFake())) { Username="user", Password="GoodPassword1!", LicenseAccepted=true };
        var succeeded = 0; vm.LoginSucceeded += () => succeeded++; await vm.LoginAsync();
        Assert.Equal(1,succeeded); Assert.Equal(string.Empty,vm.Password); Assert.Equal("USER",session.CurrentUser!.Username); Assert.True(session.CurrentUser.HasRole("Viewer"));
        var failedSession = new SessionAuthenticationService();
        var failed = new LoginViewModel(new UsernamePasswordAuthenticationService(users,audit,failedSession,new AuthenticationConfiguration(),new LoggerFake())) { Username="missing", Password="wrong", LicenseAccepted=true };
        await failed.LoginAsync();
        Assert.Equal("Invalid username or password.",failed.Message); Assert.Null(failedSession.CurrentUser);
        Assert.Equal("missing", failed.Username);
        Assert.Equal(string.Empty,failed.Password);
        var retrySession = new SessionAuthenticationService();
        var retry = new LoginViewModel(new UsernamePasswordAuthenticationService(users,audit,retrySession,new AuthenticationConfiguration(),new LoggerFake())) { Username="user", Password="wrong", LicenseAccepted=true };
        await retry.LoginAsync(); Assert.Equal(failed.Message,retry.Message); Assert.Equal("user", retry.Username); Assert.Equal(string.Empty,retry.Password); Assert.Null(retrySession.CurrentUser);
        retry.Password = "GoodPassword1!";
        await retry.LoginAsync();
        Assert.Equal("user", retry.Username); Assert.NotNull(retrySession.CurrentUser);
    }

    [Fact(DisplayName = "T-UX-05 login requires license acceptance")]
    public async Task Login_is_blocked_until_license_is_accepted()
    {
        var session = new SessionAuthenticationService();
        var vm = new LoginViewModel(new UsernamePasswordAuthenticationService(new UsersFake(), new AuditFake(), session, new AuthenticationConfiguration(), new LoggerFake()))
        {
            Username = "user", Password = "GoodPassword1!"
        };

        await vm.LoginAsync();

        Assert.Contains("accept", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(session.CurrentUser);
    }
    private sealed class UsersFake : IUserRepository { private readonly UserAccount _account; public UsersFake(){var salt=System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);_account=new UserAccount{Username="USER",DisplayName="User",PasswordSalt=salt,PasswordHash=System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("GoodPassword1!",salt,600000,System.Security.Cryptography.HashAlgorithmName.SHA256,32),Roles=new[]{"Viewer"}};} public Task<UserAccount?> FindByUsernameAsync(string n,CancellationToken t=default)=>Task.FromResult<UserAccount?>(n=="USER"?_account:null); public Task CreateAsync(UserAccount a,AuthenticationAuditEntry e,CancellationToken t=default)=>Task.CompletedTask; public Task<LoginAttemptState> RecordLoginAttemptAsync(string n,bool s,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(_account,false,null)); public Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string n,Func<UserAccount,bool> verifier,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(n=="USER"?_account:null,false,n=="USER"&&verifier(_account)?null:"InvalidPassword")); }
    private sealed class AuditFake : IAuthenticationAuditRepository { public Task WriteAsync(AuthenticationAuditEntry e,CancellationToken t=default)=>Task.CompletedTask; }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception e,string o,string c){} }
}
