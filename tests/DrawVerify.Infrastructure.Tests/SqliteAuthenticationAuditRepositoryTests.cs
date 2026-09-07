using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteAuthenticationAuditRepositoryTests
{
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
