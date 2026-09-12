using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteAuthenticationAuditRepositoryTests
{
    [Fact(DisplayName = "TD-INFRA-003 audit repository preserves role-change actor snapshots timestamp and action")]
    public async Task Role_change_audit_entry_contains_required_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-role-audit-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAuthenticationAuditRepository(path);
            var now = DateTimeOffset.UtcNow;
            await repository.WriteAsync(new("ADMIN", "TARGET", now, "RoleAssigned", "Success", RolesBefore: "[\"Maker\"]", RolesAfter: "[\"Maker\",\"Checker\"]"));
            var entry = Assert.Single((await repository.QueryAsync(new AuditLogQuery(Username: "TARGET", PageSize: 10))).Entries);
            Assert.Equal("ADMIN", entry.ActorUsername);
            Assert.Equal("TARGET", entry.Username);
            Assert.Equal("RoleAssigned", entry.Action);
            Assert.Equal("[\"Maker\"]", entry.RolesBefore);
            Assert.Equal("[\"Maker\",\"Checker\"]", entry.RolesAfter);
            Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.UtcDateTime.Kind);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-INFRA-004 failed role audit insertion leaves no successful audit row")]
    public async Task Failed_role_mutation_does_not_leave_audit_success()
    {
        await using var seed = await AuthenticationAuthorizationTestSeed.CreateAsync();
        await using (var connection = new SqliteConnection($"Data Source={seed.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER BlockRoleAuditForAuditTest BEFORE INSERT ON AuthenticationAuditLog WHEN NEW.Action='RolesUpdated' BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;";
            await trigger.ExecuteNonQueryAsync();
        }
        var target = await seed.Repository.FindByUsernameAsync("MAKER");
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Repository.ReplaceRolesAsync("MAKER", new[] { SystemRoles.Checker }, target!.RoleVersion,
            new("ADMIN", "MAKER", target.Roles, new[] { SystemRoles.Checker }, DateTimeOffset.UtcNow, "RolesUpdated")));
        var rows = (await new SqliteAuthenticationAuditRepository(seed.DatabasePath).QueryAsync(new AuditLogQuery(Username: "MAKER", Action: "RolesUpdated", PageSize: 20))).Entries;
        Assert.Empty(rows);
    }

    [Fact(DisplayName = "TD-04 preserves legacy rows and supports parameterized ordered filtering")]
    public async Task Query_reads_legacy_rows_and_applies_filters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-audit-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAuthenticationAuditRepository(path);
            await repository.WriteAsync(new("ADMIN", "USER,1", DateTimeOffset.UtcNow.AddMinutes(-2), "LoginSuccess", "Success", SourceIdentifier: "machine", ChangedFields: "[\"Email\"]"));
            await repository.WriteAsync(new("ADMIN", "OTHER", DateTimeOffset.UtcNow, "UserCreated", "Success", RolesAfter: "[\"Viewer\"]"));
            var result = await repository.QueryAsync(new AuditLogQuery(Action: "LoginSuccess", Username: "USER,1", ActorUsername: "ADMIN", PageSize: 10));
            var entry = Assert.Single(result.Entries);
            Assert.Equal("USER,1", entry.Username);
            Assert.Equal("machine", entry.SourceIdentifier);
            Assert.Equal(1, result.TotalCount);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-04 retention keeps indefinite records and purges only expired records")]
    public async Task Retention_zero_keeps_rows_and_positive_days_purges_expired_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-retention-{Guid.NewGuid():N}.db");
        try
        {
            var audit = new SqliteAuthenticationAuditRepository(path);
            await audit.WriteAsync(new(null, "OLD", DateTimeOffset.UtcNow.AddDays(-10), "LoginFailure", "Failure"));
            await audit.WriteAsync(new(null, "NEW", DateTimeOffset.UtcNow, "LoginSuccess", "Success"));
            var retention = new SqliteAuditRetentionRepository(path);
            Assert.Equal(0, await retention.ApplyAsync(0));
            Assert.Equal(1, await retention.ApplyAsync(5));
            Assert.Equal(1, (await audit.QueryAsync(new AuditLogQuery(PageSize: 10))).TotalCount);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
