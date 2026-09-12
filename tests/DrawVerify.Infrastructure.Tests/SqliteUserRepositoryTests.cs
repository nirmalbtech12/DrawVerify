using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteUserRepositoryTests
{
    [Fact(DisplayName = "TD-INFRA-001 replacement persists add-only remove-only mixed and idempotent sets atomically")]
    public async Task Role_replacement_persists_canonical_sets_and_versions()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var target = await seed.Repository.FindByUsernameAsync("MAKER");
        var added = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Checker", "Maker" }, target!.RoleVersion, Audit("MAKER", target.Roles, new[] { "Checker", "Maker" }, "RoleAssigned"));
        Assert.True(added.Succeeded);
        var afterAdd = await seed.Repository.FindByUsernameAsync("MAKER");
        Assert.Equal(new[] { "Checker", "Maker" }, afterAdd!.Roles);
        var same = await seed.Repository.ReplaceRolesAsync("MAKER", afterAdd.Roles, afterAdd.RoleVersion, Audit("MAKER", afterAdd.Roles, afterAdd.Roles, "RolesUpdated"));
        Assert.True(same.Succeeded);
        Assert.Equal(afterAdd.RoleVersion, same.RoleVersion);
        var removed = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Checker" }, afterAdd.RoleVersion, Audit("MAKER", afterAdd.Roles, new[] { "Checker" }, "RoleRemoved"));
        Assert.True(removed.Succeeded);
        Assert.Equal(new[] { "Checker" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
    }

    [Fact(DisplayName = "TD-INFRA-002 stale missing invalid and last critical role changes are rejected before mutation")]
    public async Task Invalid_and_critical_role_replacements_preserve_pre_state()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var maker = await seed.Repository.FindByUsernameAsync("MAKER");
        var stale = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Checker" }, maker!.RoleVersion - 1, Audit("ADMIN", maker.Roles, new[] { "Checker" }, "RolesUpdated"));
        Assert.Equal(RoleReplacementFailure.StaleRoleVersion, stale.Failure);
        var invalid = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Maker", "maker" }, maker.RoleVersion, Audit("ADMIN", maker.Roles, new[] { "Maker", "maker" }, "RolesUpdated"));
        Assert.Equal(RoleReplacementFailure.InvalidRoles, invalid.Failure);
        var missing = await seed.Repository.ReplaceRolesAsync("MISSING", new[] { "Maker" }, 0, Audit("ADMIN", Array.Empty<string>(), new[] { "Maker" }, "RoleAssigned"));
        Assert.Equal(RoleReplacementFailure.UserNotFound, missing.Failure);
        var approver = await seed.Repository.FindByUsernameAsync("APPROVER");
        var lastApprover = await seed.Repository.ReplaceRolesAsync("APPROVER", Array.Empty<string>(), approver!.RoleVersion, Audit("ADMIN", approver.Roles, Array.Empty<string>(), "RoleRemoved"));
        Assert.Equal(RoleReplacementFailure.LastApprover, lastApprover.Failure);
        Assert.Equal(new[] { "Approver" }, (await seed.Repository.FindByUsernameAsync("APPROVER"))!.Roles);
        var admin = await seed.Repository.FindByUsernameAsync("ADMIN");
        var lastAdmin = await seed.Repository.ReplaceRolesAsync("ADMIN", Array.Empty<string>(), admin!.RoleVersion, Audit("ADMIN", admin.Roles, Array.Empty<string>(), "RoleRemoved"));
        Assert.Equal(RoleReplacementFailure.LastAdministrator, lastAdmin.Failure);
        Assert.Equal(new[] { "Admin" }, (await seed.Repository.FindByUsernameAsync("ADMIN"))!.Roles);
    }

    [Fact(DisplayName = "TD-INFRA-003 successful role mutations persist complete audit classification")]
    public async Task Role_mutations_write_required_audit_fields()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        var target = await seed.Repository.FindByUsernameAsync("MAKER");
        var timestamp = DateTimeOffset.UtcNow;
        Assert.True((await seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Maker", "Checker" }, target!.RoleVersion, new("ADMIN", "MAKER", target.Roles, new[] { "Maker", "Checker" }, timestamp, "RoleAssigned"))).Succeeded);
        var rows = (await new SqliteAuthenticationAuditRepository(seed.DatabasePath).QueryAsync(new AuditLogQuery(Username: "MAKER", PageSize: 50))).Entries;
        var row = Assert.Single(rows.Where(x => x.Action == "RoleAssigned"));
        Assert.Equal("ADMIN", row.ActorUsername);
        Assert.Equal("MAKER", row.Username);
        Assert.True(row.TimestampUtc.UtcDateTime.Kind == DateTimeKind.Utc);
        Assert.Equal(new[] { "Maker" }, JsonSerializer.Deserialize<string[]>(row.RolesBefore));
        Assert.Equal(new[] { "Checker", "Maker" }, JsonSerializer.Deserialize<string[]>(row.RolesAfter));
    }

    [Fact(DisplayName = "TD-INFRA-004 audit failure rolls back role rows and version")]
    public async Task Audit_failure_rolls_back_role_mutation()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        await using (var connection = new SqliteConnection($"Data Source={seed.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER BlockRoleAudit BEFORE INSERT ON AuthenticationAuditLog WHEN NEW.Action='RolesUpdated' BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;";
            await trigger.ExecuteNonQueryAsync();
        }
        var before = await seed.Repository.FindByUsernameAsync("MAKER");
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Repository.ReplaceRolesAsync("MAKER", new[] { "Checker" }, before!.RoleVersion, Audit("MAKER", before.Roles, new[] { "Checker" }, "RolesUpdated")));
        var after = await seed.Repository.FindByUsernameAsync("MAKER");
        Assert.Equal(before.Roles, after!.Roles);
        Assert.Equal(before.RoleVersion, after.RoleVersion);
    }

    private static RoleAssignmentAuditEntry Audit(string username, IReadOnlyList<string> before, IReadOnlyList<string> after, string action)
        => new("ADMIN", username, before, after, DateTimeOffset.UtcNow, action);

    [Fact(DisplayName = "TD-01 persists profile and deactivation audit data atomically")]
    public async Task Profile_and_deactivation_are_transactionally_audited()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-lifecycle-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteUserRepository(path);
            var salt = new byte[16];
            await repository.CreateAsync(new UserAccount { Username = "TARGET", DisplayName = "Target", PasswordSalt = salt, PasswordHash = new byte[32], PasswordIterations = 1, Roles = new[] { "Viewer" } }, new("ADMIN", "TARGET", DateTimeOffset.UtcNow, "UserCreated", "Success", RolesAfter: "[\"Viewer\"]"));
            await repository.UpdateProfileAsync("TARGET", "Changed", "target@example.test", true, new("ADMIN", "TARGET", DateTimeOffset.UtcNow, "UserModified", "Success", ChangedFields: "[\"DisplayName\",\"Email\",\"PasswordReset\"]"));
            await repository.DeactivateAsync("TARGET", "retired", new("ADMIN", "TARGET", DateTimeOffset.UtcNow, "UserDeactivated", "Success", DeactivationReason: "retired"));
            var target = await repository.FindByUsernameAsync("TARGET");
            Assert.False(target!.IsActive); Assert.Equal("Changed", target.DisplayName); Assert.Equal("target@example.test", target.Email);
            var audit = new SqliteAuthenticationAuditRepository(path);
            var rows = (await audit.QueryAsync(new AuditLogQuery(PageSize: 20))).Entries;
            Assert.Contains(rows, x => x.Action == "UserModified" && x.ChangedFields!.Contains("PasswordReset"));
            Assert.Contains(rows, x => x.Action == "UserDeactivated" && x.DeactivationReason == "retired");
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-02 classifies add-only, remove-only, and mixed role replacements atomically")]
    public async Task Seed_and_role_replacement_preserve_authorization_invariants()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-auth-seed-{Guid.NewGuid():N}.db");
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync(path);
        await using var secondSeed = await AuthenticationAuthorizationTestSeed.CreateAsync(path);

        var accounts = await seed.Repository.ListAsync();
        Assert.Equal(new[] { "ADMIN", "APPROVER", "CHECKER", "MAKER", "TESTUSER" }, accounts.Select(x => x.Username));
        Assert.Equal("Admin", (await seed.Repository.FindByUsernameAsync("ADMIN"))!.Roles.Single());
        Assert.Equal("GoodPassword1!", AuthenticationAuthorizationTestSeed.Password);

        var maker = await seed.Repository.FindByUsernameAsync("MAKER");
        var change = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Checker, SystemRoles.Viewer }, maker!.RoleVersion,
            new("ADMIN", "MAKER", maker.Roles, new[] { SystemRoles.Checker, SystemRoles.Viewer }, DateTimeOffset.UtcNow, "RolesUpdated"));
        Assert.True(change.Succeeded);
        Assert.Equal(new[] { "Checker", "Viewer" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);
        Assert.Equal(1, (await seed.Repository.FindByUsernameAsync("MAKER"))!.RoleVersion);

        var duplicate = Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalize(new[] { "Maker", "maker" }));
        Assert.Contains("Duplicate", duplicate.Message);

        var admin = await seed.Repository.FindByUsernameAsync("ADMIN");
        var lastAdmin = await seed.Repository.ReplaceRolesAsync("ADMIN", Array.Empty<string>(), admin!.RoleVersion,
            new("ADMIN", "ADMIN", admin.Roles, Array.Empty<string>(), DateTimeOffset.UtcNow, "RoleRemoved"));
        Assert.False(lastAdmin.Succeeded);
        Assert.Equal(RoleReplacementFailure.LastAdministrator, lastAdmin.Failure);
        Assert.Equal("Admin", (await seed.Repository.FindByUsernameAsync("ADMIN"))!.Roles.Single());

        var stale = await seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Maker }, 0,
            new("ADMIN", "MAKER", Array.Empty<string>(), new[] { SystemRoles.Maker }, DateTimeOffset.UtcNow, "RolesUpdated"));
        Assert.False(stale.Succeeded);
        Assert.Equal(RoleReplacementFailure.StaleRoleVersion, stale.Failure);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER BlockRoleAudit BEFORE INSERT ON AuthenticationAuditLog WHEN NEW.Action='RolesUpdated' BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;";
            await trigger.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Maker }, 1,
            new("ADMIN", "MAKER", new[] { SystemRoles.Checker, SystemRoles.Viewer }, new[] { SystemRoles.Maker }, DateTimeOffset.UtcNow, "RolesUpdated")));
        Assert.Equal(new[] { "Checker", "Viewer" }, (await seed.Repository.FindByUsernameAsync("MAKER"))!.Roles);

        await using var auditConnection = new SqliteConnection($"Data Source={path}");
        await auditConnection.OpenAsync();
        await using var audit = auditConnection.CreateCommand();
        audit.CommandText = "SELECT ActorUsername,Username,TimestampUtc,Action,RolesBefore,RolesAfter FROM AuthenticationAuditLog WHERE Username='MAKER' AND Action='RolesUpdated'";
        await using var reader = await audit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ADMIN", reader.GetString(0));
        Assert.Equal("MAKER", reader.GetString(1));
        Assert.True(DateTimeOffset.TryParse(reader.GetString(2), out _));
        Assert.Equal(new[] { "Maker" }, JsonSerializer.Deserialize<string[]>(reader.GetString(4)));
        Assert.Equal(new[] { "Checker", "Viewer" }, JsonSerializer.Deserialize<string[]>(reader.GetString(5)));
    }
}
