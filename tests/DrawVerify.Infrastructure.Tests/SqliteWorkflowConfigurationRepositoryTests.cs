using System.Text.Json;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteWorkflowConfigurationRepositoryTests
{
    [Fact(DisplayName = "TD-INF-001 migration creates additive configuration tables")]
    public async Task Migration_is_additive()
    {
        var path = TempPath();
        try { await using (var c = Open(path)) { var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE Workflow(Submissions TEXT);"; await cmd.ExecuteNonQueryAsync(); } await new SqliteWorkflowConfigurationRepository(path).GetCurrentAsync(); await using var check = Open(path); Assert.Equal(1, await Scalar(check, "SELECT COUNT(*) FROM sqlite_master WHERE name='WorkflowConfigurationVersions'")); Assert.Equal(1, await Scalar(check, "SELECT COUNT(*) FROM sqlite_master WHERE name='WorkflowConfigurationAudit'")); }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-002 complete payloads round-trip and historical versions remain readable")]
    public async Task Versions_round_trip()
    {
        var path = TempPath();
        try { var repository = new SqliteWorkflowConfigurationRepository(path); var v1 = Baseline() with { Version = 1 }; var v2 = Baseline() with { Version = 2, StatusLabels = new Dictionary<WorkflowStatus, string> { [WorkflowStatus.UnderReview] = "In Review" } }; await repository.SaveAsync(v1, Audit(1), null); await repository.SaveAsync(v2, Audit(2), 1); Assert.Equal(2, (await repository.GetCurrentAsync())!.Version); Assert.Equal("In Review", (await repository.GetVersionAsync(2))!.StatusLabels[WorkflowStatus.UnderReview]); Assert.Equal(1, (await repository.GetVersionAsync(1))!.Version); }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-003 configuration save commits payload pointer and audit together")]
    public async Task Successful_save_is_atomic()
    {
        var path = TempPath();
        try { var repository = new SqliteWorkflowConfigurationRepository(path); await repository.SaveAsync(Baseline(), Audit(1), null); await using var c = Open(path); Assert.Equal(1, await Scalar(c, "SELECT COUNT(*) FROM WorkflowConfigurationVersions")); Assert.Equal(1, await Scalar(c, "SELECT COUNT(*) FROM WorkflowConfigurationAudit")); Assert.Equal(1, await Scalar(c, "SELECT COUNT(*) FROM WorkflowConfigurationVersions WHERE IsCurrent=1")); }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-004 audit fields and optimistic version conflict are preserved")]
    public async Task Audit_and_conflict_are_explicit()
    {
        var path = TempPath();
        try { var repository = new SqliteWorkflowConfigurationRepository(path); var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); await repository.SaveAsync(Baseline(), new("admin", timestamp, null, 1, "old", "new"), null); await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(Baseline() with { Version = 2 }, new("other", timestamp, 1, 2, "old2", "new2"), 99)); await using var c = Open(path); Assert.Equal(1, await Scalar(c, "SELECT COUNT(*) FROM WorkflowConfigurationAudit")); Assert.Equal("admin", await Text(c, "SELECT ActorId FROM WorkflowConfigurationAudit")); Assert.Equal("old", await Text(c, "SELECT PreviousValue FROM WorkflowConfigurationAudit")); Assert.Equal(1, await Scalar(c, "SELECT Version FROM WorkflowConfigurationVersions WHERE IsCurrent=1")); }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-005 legacy nullable workflow version references remain compatible")]
    public async Task Legacy_rows_remain_readable_after_migration()
    {
        var path = TempPath();
        try { await using (var c = Open(path)) { var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE WorkflowInstances(Id INTEGER PRIMARY KEY, ConfigurationVersion INTEGER NULL); INSERT INTO WorkflowInstances(Id,ConfigurationVersion) VALUES(1,NULL),(2,7);"; await cmd.ExecuteNonQueryAsync(); } await new SqliteWorkflowConfigurationRepository(path).GetCurrentAsync(); await using var check = Open(path); Assert.Equal(2, await Scalar(check, "SELECT COUNT(*) FROM WorkflowInstances")); Assert.Equal(1, await Scalar(check, "SELECT COUNT(*) FROM WorkflowInstances WHERE ConfigurationVersion IS NULL")); Assert.Equal(7, await Scalar(check, "SELECT ConfigurationVersion FROM WorkflowInstances WHERE Id=2")); }
        finally { Cleanup(path); }
    }

    private static WorkflowConfiguration Baseline() => new() { PermittedInitialStates = [WorkflowStatus.Draft, WorkflowStatus.InPreparation], DuplicateSubmissionStates = [WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved] };
    private static WorkflowConfigurationAudit Audit(int version) => new("admin", new DateTime(2026, 1, 1, 0, 0, version, DateTimeKind.Utc), version == 1 ? null : version - 1, version, "old", "new");
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
    private static SqliteConnection Open(string path) { var c = new SqliteConnection($"Data Source={path}"); c.Open(); return c; }
    private static async Task<long> Scalar(SqliteConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task<string> Text(SqliteConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return (string)(await cmd.ExecuteScalarAsync())!; }
    private static void Cleanup(string path) { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}
