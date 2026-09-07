using System.Security.Cryptography;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class UsernamePasswordAuthenticationServiceTests
{
    [Fact(DisplayName = "TD-03 and TD-07 await authentication and logout audit writes")]
    public async Task Successful_login_and_logout_update_session_and_audit()
    {
        var users = new UsersFake(Account("USER", "GoodPassword1!", "Viewer")); var audit = new AuditFake(); var session = new SessionAuthenticationService(); var service = Service(users, audit, session);
        var login = await service.LoginAsync("user", "GoodPassword1!");
        Assert.True(login.Succeeded); Assert.Equal("USER", session.CurrentUser!.Username); Assert.True(session.CurrentUser.HasRole("viewer")); Assert.Equal("LoginSuccess", Assert.Single(audit.Entries).Action); Assert.False(string.IsNullOrWhiteSpace(audit.Entries[0].SourceIdentifier));
        var logout = await service.LogoutAsync(); Assert.True(logout.Succeeded); Assert.Null(session.CurrentUser); Assert.Equal("Logout", audit.Entries[1].Action);
    }

    [Fact]
    public async Task Unknown_and_wrong_password_have_same_safe_message_and_no_session()
    {
        var audit = new AuditFake(); var session = new SessionAuthenticationService(); var service = Service(new UsersFake(Account("USER", "GoodPassword1!")), audit, session);
        var unknown = await service.LoginAsync("missing", "wrong"); var wrong = await service.LoginAsync("user", "wrong");
        Assert.Equal(unknown.Message, wrong.Message); Assert.Equal(AuthenticationResultKind.InvalidCredentials, unknown.Kind); Assert.Null(session.CurrentUser); Assert.Equal(new[] { "InvalidUsername", "InvalidPassword" }, audit.Entries.Select(x=>x.FailureReason)); Assert.All(audit.Entries, x => Assert.Equal("LoginFailure", x.Action));
    }

    [Fact]
    public async Task Enabled_lockout_blocks_until_expiry_and_success_resets_attempts()
    {
        var account = Account("LOCKED", "GoodPassword1!"); var users = new UsersFake(account); var audit = new AuditFake(); var session = new SessionAuthenticationService(); var service = Service(users, audit, session, new AuthenticationConfiguration { LockoutThreshold=2, LockoutDuration=TimeSpan.FromMinutes(5) });
        Assert.Equal(AuthenticationResultKind.InvalidCredentials, (await service.LoginAsync("locked", "bad")).Kind); Assert.Equal(AuthenticationResultKind.AccountLocked, (await service.LoginAsync("locked", "bad")).Kind); Assert.Equal(AuthenticationResultKind.AccountLocked, (await service.LoginAsync("locked", "GoodPassword1!")).Kind); Assert.Single(audit.Entries, x=>x.Action=="AccountLocked"); Assert.Equal(2, audit.Entries.Single(x=>x.Action=="AccountLocked").FailedAttemptCount);
    }

    [Fact]
    public async Task Logout_audit_failure_does_not_clear_session_or_report_success()
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", Array.Empty<string>())); var audit = new AuditFake { Throw=true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new UsersFake(), audit, session).LogoutAsync()); Assert.NotNull(session.CurrentUser);
    }

    private static UsernamePasswordAuthenticationService Service(UsersFake u, AuditFake a, ISessionAuthenticationService s, AuthenticationConfiguration? c=null) => new(u,a,s,c??new AuthenticationConfiguration(),new LoggerFake());
    private static UserAccount Account(string username,string password,params string[] roles) { var salt=RandomNumberGenerator.GetBytes(16); return new UserAccount { Username=username,DisplayName=username,PasswordSalt=salt,PasswordHash=Rfc2898DeriveBytes.Pbkdf2(password,salt,600000,HashAlgorithmName.SHA256,32),Roles=roles }; }
    private sealed class UsersFake(params UserAccount[] initial) : IUserRepository { public List<UserAccount> Accounts { get; }=initial.ToList(); public Task<UserAccount?> FindByUsernameAsync(string n,CancellationToken t=default)=>Task.FromResult(Accounts.SingleOrDefault(x=>x.Username==n)); public Task CreateAsync(UserAccount a,AuthenticationAuditEntry e,CancellationToken t=default)=>Task.CompletedTask; public Task<LoginAttemptState> RecordLoginAttemptAsync(string n,bool success,int threshold,TimeSpan duration,DateTimeOffset now,CancellationToken t=default){var a=Accounts.Single(x=>x.Username==n);if(success){a.FailedAttempts=0;a.LockedUntilUtc=null;}else{a.FailedAttempts++;if(threshold>0&&a.FailedAttempts>=threshold)a.LockedUntilUtc=now.Add(duration);}return Task.FromResult(new LoginAttemptState(a,a.LockedUntilUtc>now,success?null:a.LockedUntilUtc>now?"AccountLocked":"InvalidPassword"));} public async Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string n,Func<UserAccount,bool> verifier,int threshold,TimeSpan duration,DateTimeOffset now,CancellationToken t=default){var a=await FindByUsernameAsync(n,t);if(a is null)return new(null,false,"InvalidUsername");if(a.LockedUntilUtc>now)return new(null,true,"AccountLocked");return await RecordLoginAttemptAsync(n,verifier(a),threshold,duration,now,t);} }
    private sealed class AuditFake : IAuthenticationAuditRepository { public List<AuthenticationAuditEntry> Entries { get; }=[]; public bool Throw { get; init; } public Task WriteAsync(AuthenticationAuditEntry e,CancellationToken t=default){if(Throw)throw new InvalidOperationException("audit unavailable");Entries.Add(e);return Task.CompletedTask;} }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception e,string o,string c){} }
}
