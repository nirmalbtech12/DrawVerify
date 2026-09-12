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
    [Fact(DisplayName = "TD-APP-001 Admin can replace one or more roles, remove roles, and repeat idempotently")]
    public async Task Admin_role_replacement_supports_single_multiple_remove_and_idempotent_changes()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var session = await LoginAdminAsync(seed, audit);
        var service = new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        var target = await seed.Repository.FindByUsernameAsync("MAKER");
        var added = await service.SetUserRolesAsync(" maker ", new[] { SystemRoles.Checker, SystemRoles.Maker }, target!.RoleVersion);
        Assert.True(added.Succeeded);
        var current = await seed.Repository.FindByUsernameAsync("MAKER");
        Assert.Equal(new[] { SystemRoles.Checker, SystemRoles.Maker }, current!.Roles);
        var unchanged = await service.SetUserRolesAsync("MAKER", current.Roles, current.RoleVersion);
        Assert.True(unchanged.Succeeded);
        Assert.Equal(current.RoleVersion, unchanged.Succeeded ? current.RoleVersion : -1);
        var removed = await service.SetUserRolesAsync("MAKER", new[] { SystemRoles.Checker }, current.RoleVersion);
        Assert.True(removed.Succeeded);
        Assert.Equal(new[] { SystemRoles.Checker }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
    }

    [Theory(DisplayName = "TD-APP-002 unauthenticated and non-admin callers are denied")]
    [InlineData(SystemRoles.Maker)]
    [InlineData(SystemRoles.Checker)]
    [InlineData(SystemRoles.Approver)]
    [InlineData(SystemRoles.Viewer)]
    public async Task Non_admin_role_operations_are_denied(string role)
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var session = new SessionAuthenticationService();
        session.SignIn(new("NONADMIN", new[] { role }));
        var repository = new RecordingUserRepository(seed.Repository);
        var service = new UserAdministrationService(repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        var result = await service.SetUserRolesAsync("MAKER", new[] { SystemRoles.Checker });
        Assert.False(result.Succeeded);
        Assert.Contains("administrator", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repository.ReplaceCalls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListUsersAsync());
    }

    [Fact(DisplayName = "TD-APP-003 failures are clear and self-role replacement refreshes the session")]
    public async Task Role_failures_do_not_refresh_session_but_self_success_does()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var audit = new SqliteAuthenticationAuditRepository(seed.DatabasePath);
        var session = await LoginAdminAsync(seed, audit);
        var service = new UserAdministrationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        var invalid = await service.SetUserRolesAsync("MAKER", new[] { "unknown" });
        Assert.False(invalid.Succeeded);
        Assert.Contains("unknown", invalid.Message!, StringComparison.OrdinalIgnoreCase);
        var target = await seed.Repository.FindByUsernameAsync("ADMIN");
        var self = await service.SetUserRolesAsync("ADMIN", new[] { SystemRoles.Admin, SystemRoles.Approver }, target!.RoleVersion);
        Assert.True(self.Succeeded);
        Assert.Equal(new[] { SystemRoles.Admin, SystemRoles.Approver }, session.CurrentUser!.Roles);
        Assert.Equal(1, session.CurrentUser.RoleVersion);
        var stale = await service.SetUserRolesAsync("ADMIN", new[] { SystemRoles.Admin }, target.RoleVersion);
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.Message!, StringComparison.OrdinalIgnoreCase);
    }

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

    private static async Task<SessionAuthenticationService> LoginAdminAsync(AuthenticationAuthorizationTestSeed seed, SqliteAuthenticationAuditRepository audit)
    {
        var session = new SessionAuthenticationService();
        var login = new UsernamePasswordAuthenticationService(seed.Repository, audit, session, new AuthenticationConfiguration(), new LoggerFake());
        Assert.True((await login.LoginAsync("admin", AuthenticationAuthorizationTestSeed.Password)).Succeeded);
        return session;
    }

    private sealed class RecordingUserRepository(IUserRepository inner) : IUserRepository
    {
        public int ReplaceCalls { get; private set; }
        public Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default) => inner.FindByUsernameAsync(normalizedUsername, cancellationToken);
        public Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default) => inner.CreateAsync(account, audit, cancellationToken);
        public Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.RecordLoginAttemptAsync(normalizedUsername, successful, lockoutThreshold, lockoutDuration, now, cancellationToken);
        public Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string normalizedUsername, Func<UserAccount, bool> verifier, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.VerifyAndRecordLoginAttemptAsync(normalizedUsername, verifier, lockoutThreshold, lockoutDuration, now, cancellationToken);
        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task UpdateProfileAsync(string normalizedUsername, string? displayName, string? email, bool passwordReset, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default) => inner.UpdateProfileAsync(normalizedUsername, displayName, email, passwordReset, audit, cancellationToken);
        public Task DeactivateAsync(string normalizedUsername, string? reason, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default) => inner.DeactivateAsync(normalizedUsername, reason, audit, cancellationToken);
        public Task<RoleReplacementResult> ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, long expectedRoleVersion, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default) { ReplaceCalls++; return inner.ReplaceRolesAsync(normalizedUsername, roles, expectedRoleVersion, audit, cancellationToken); }
    }
}
