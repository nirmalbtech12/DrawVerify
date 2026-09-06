using System.Security.Cryptography;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact(DisplayName = "TD-APP-01 creates an admin-requested user with audit and empty roles")]
    public async Task Create_user_succeeds_and_records_safe_audit()
    {
        var users = new UsersFake(); var audit = new AuditFake(); users.Audit = audit; var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }));
        var result = await Service(users, audit, session).CreateUserAsync(new(" new.user ", "New User", "GoodPassword1!"));
        Assert.True(result.Succeeded); Assert.Single(users.Accounts); Assert.Empty(users.Accounts[0].Roles);
        var entry = Assert.Single(audit.Entries); Assert.Equal("ADMIN", entry.ActorUsername); Assert.Equal("NEW.USER", entry.Username); Assert.Equal("UserCreated", entry.Action); Assert.Equal("Success", entry.Result); Assert.NotEqual(default, entry.TimestampUtc);
    }

    [Fact]
    public async Task Required_fields_policy_duplicate_and_authorization_fail_before_persistence()
    {
        var users = new UsersFake { Duplicate = true }; var audit = new AuditFake(); users.Audit = audit; var anonymous = new SessionAuthenticationService(); var service = Service(users, audit, anonymous);
        Assert.Equal(AuthenticationResultKind.ValidationFailure, (await service.CreateUserAsync(new("x", "x", "GoodPassword1!"))).Kind);
        var admin = new SessionAuthenticationService(); admin.SignIn(new AuthenticatedUser("admin", new[] { "Admin" })); service = Service(users, audit, admin);
        Assert.Contains("Username is required", (await service.CreateUserAsync(new(" ", "x", "GoodPassword1!"))).Message);
        Assert.Contains("Display name is required", (await service.CreateUserAsync(new("x", " ", "GoodPassword1!"))).Message);
        Assert.Contains("at least", (await service.CreateUserAsync(new("x", "x", "bad"))).Message);
        Assert.Equal(AuthenticationResultKind.DuplicateUsername, (await service.CreateUserAsync(new("x", "x", "GoodPassword1!"))).Kind);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Audit_failure_is_visible_and_not_converted_to_success()
    {
        var users = new UsersFake(); var audit = new AuditFake { Throw = true }; users.Audit = audit; var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", new[] { "Admin" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(users, audit, session).CreateUserAsync(new("new.user", "New", "GoodPassword1!")));
    }

    [Fact(DisplayName = "TD-APP-001 permits only Admin role replacement and derives audit actions")]
    public async Task Admin_can_assign_any_combination_and_audit_before_after_values()
    {
        var users = new UsersFake();
        users.Accounts.Add(Account("ADMIN", new[] { "Admin" }));
        users.Accounts.Add(Account("TARGET", new[] { "Maker" }));
        var audit = new AuditFake();
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }));
        var result = await Service(users, audit, session).SetUserRolesAsync(" target ", new[] { "checker", "Viewer" });
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "Checker", "Viewer" }, users.AssignedRoles);
        Assert.Equal("ADMIN", users.Replacement!.ActorUsername);
        Assert.Equal("TARGET", users.Replacement.AffectedUsername);
        Assert.Empty(users.Replacement.RolesBefore);
        Assert.Equal(new[] { "Checker", "Viewer" }, users.Replacement.RolesAfter);
        Assert.Equal("RolesUpdated", users.Replacement.ActionType);
        Assert.NotEqual(default, users.Replacement.TimestampUtc);
    }

    [Fact]
    public async Task Invalid_roles_and_non_admin_are_rejected_before_repository_mutation()
    {
        var users = new UsersFake(); users.Accounts.Add(Account("TARGET", Array.Empty<string>()));
        var audit = new AuditFake();
        var viewer = new SessionAuthenticationService(); viewer.SignIn(new AuthenticatedUser("VIEWER", new[] { "Viewer" }));
        var service = Service(users, audit, viewer);
        Assert.False((await service.SetUserRolesAsync("TARGET", new[] { "Admin" })).Succeeded);
        var admin = new SessionAuthenticationService(); admin.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }));
        service = Service(users, audit, admin);
        Assert.False((await service.SetUserRolesAsync("TARGET", new[] { "", "Maker" })).Succeeded);
        Assert.False((await service.SetUserRolesAsync("TARGET", new[] { "Maker", "maker" })).Succeeded);
        Assert.False((await service.SetUserRolesAsync("MISSING", new[] { "Maker" })).Succeeded);
        Assert.Null(users.Replacement);
    }

    private static UserAccount Account(string username, IReadOnlyList<string> roles) => new() { Username = username, DisplayName = username, PasswordSalt = new byte[16], PasswordHash = new byte[32], Roles = roles };

    private static UserAdministrationService Service(UsersFake users, AuditFake audit, ISessionAuthenticationService session) => new(users, audit, session, new AuthenticationConfiguration(), new LoggerFake());
    private sealed class UsersFake : IUserRepository { public List<UserAccount> Accounts { get; } = new(); public bool Duplicate { get; init; } public AuditFake? Audit { get; set; } public RoleAssignmentAuditEntry? Replacement { get; private set; } public IReadOnlyList<string> AssignedRoles { get; private set; } = Array.Empty<string>(); public Task<UserAccount?> FindByUsernameAsync(string n, CancellationToken t=default) => Task.FromResult(Accounts.SingleOrDefault(x=>x.Username==n)); public Task CreateAsync(UserAccount a, AuthenticationAuditEntry e, CancellationToken t=default) { if(Duplicate || Accounts.Any(x=>x.Username==a.Username)) throw new DuplicateUsernameException(); if(Audit?.Throw == true) throw new InvalidOperationException("audit unavailable"); Accounts.Add(a); Audit?.Entries.Add(e); return Task.CompletedTask; } public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<UserAccount>>(Accounts); public Task ReplaceRolesAsync(string n,IReadOnlyList<string> roles,RoleAssignmentAuditEntry audit,CancellationToken t=default){if(Accounts.All(x=>x.Username!=n)) throw new KeyNotFoundException($"User '{n}' was not found."); Replacement=audit;AssignedRoles=roles;return Task.CompletedTask;} public Task<LoginAttemptState> RecordLoginAttemptAsync(string n,bool s,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(Accounts.SingleOrDefault(x=>x.Username==n),false,null)); public Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string n,Func<UserAccount,bool> verifier,int th,TimeSpan d,DateTimeOffset now,CancellationToken t=default)=>Task.FromResult(new LoginAttemptState(Accounts.SingleOrDefault(x=>x.Username==n),false,null)); }
    private sealed class AuditFake : IAuthenticationAuditRepository { public List<AuthenticationAuditEntry> Entries { get; } = new(); public bool Throw { get; init; } public Task WriteAsync(AuthenticationAuditEntry e,CancellationToken t=default) { if(Throw) throw new InvalidOperationException("audit unavailable"); Entries.Add(e); return Task.CompletedTask; } }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception e,string o,string c) { } }
}
