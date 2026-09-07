using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteUserRepositoryTests
{
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
