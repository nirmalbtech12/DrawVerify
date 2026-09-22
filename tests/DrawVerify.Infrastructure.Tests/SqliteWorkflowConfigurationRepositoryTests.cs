using System.Text.Json;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Notifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteWorkflowConfigurationRepositoryTests
{
    [Fact(DisplayName = "TSD-06 administrator notification persists actionable invalid-mapping context")]
    public async Task Administrator_notification_round_trips_invalid_mapping_context()
    {
        var path = TempPath();
        var templateId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        try
        {
            _ = new SqliteWorkflowRepository(path, configuration: Baseline());
            var notifications = new InAppNotificationService(path);
            await notifications.NotifyAdministratorAsync(templateId, 3, packageId, 2, placeholderId, "Unmapped Approver", "role is invalid");
            var stored = Assert.Single(await notifications.GetForUserAsync("Admin"));
            Assert.Contains(templateId.ToString(), stored.Message, StringComparison.Ordinal);
            Assert.Contains("version 3", stored.Message, StringComparison.Ordinal);
            Assert.Contains(packageId.ToString(), stored.Message, StringComparison.Ordinal);
            Assert.Contains("revision 2", stored.Message, StringComparison.Ordinal);
            Assert.Contains(placeholderId.ToString(), stored.Message, StringComparison.Ordinal);
            Assert.Contains("Unmapped Approver", stored.Message, StringComparison.Ordinal);
            Assert.Contains("role is invalid", stored.Message, StringComparison.Ordinal);
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TSD-06 explicit unmapped-placeholder policy persists and reloads")]
    public async Task Unmapped_placeholder_policy_round_trips_explicitly()
    {
        var path = TempPath();
        try
        {
            var configuration = Baseline() with { UnmappedSignaturePlaceholderPolicy = UnmappedSignaturePlaceholderPolicy.Skip };
            var repository = new SqliteWorkflowConfigurationRepository(path);
            await repository.SaveAsync(configuration, Audit(1), null);
            var restored = await repository.GetCurrentAsync();
            Assert.NotNull(restored);
            Assert.Equal(UnmappedSignaturePlaceholderPolicy.Skip, restored!.UnmappedSignaturePlaceholderPolicy);
        }
        finally { Cleanup(path); }
    }
    [Fact(DisplayName = "TSD-CHANNEL-DISPATCH configuration persists supported InApp channel")]
    public async Task TSD_CHANNEL_DISPATCH_round_trips_inapp_preference()
    {
        var path = TempPath();
        try
        {
            var configuration = Baseline() with { Notifications = [new() { Event = WorkflowNotificationEvent.Rejection, RecipientRoles = ["Maker"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "rejected" }] };
            var repository = new SqliteWorkflowConfigurationRepository(path); await repository.SaveAsync(configuration, Audit(1), null);
            var loaded = await repository.GetCurrentAsync(); var rule = Assert.Single(loaded!.Notifications); Assert.Equal(WorkflowNotificationChannel.InApp, Assert.Single(rule.Channels)); Assert.Contains("Maker", rule.RecipientRoles);
        }
        finally { Cleanup(path); }
    }
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

    [Fact(DisplayName = "TD-INF-004 audit stores timestamp and complete old/new values")]
    public async Task Audit_contains_complete_change_metadata()
    {
        var path = TempPath();
        var timestamp = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        try
        {
            await new SqliteWorkflowConfigurationRepository(path).SaveAsync(
                Baseline() with { Version = 1 },
                new WorkflowConfigurationAudit("admin-42", timestamp, null, 1, "previous-json", "new-json"), null);
            await using var connection = Open(path);
            Assert.Equal("admin-42", await Text(connection, "SELECT ActorId FROM WorkflowConfigurationAudit WHERE NewVersion=1"));
            Assert.Equal(timestamp.ToString("O"), await Text(connection, "SELECT TimestampUtc FROM WorkflowConfigurationAudit WHERE NewVersion=1"));
            Assert.Equal("previous-json", await Text(connection, "SELECT PreviousValue FROM WorkflowConfigurationAudit WHERE NewVersion=1"));
            Assert.Equal("new-json", await Text(connection, "SELECT NewValue FROM WorkflowConfigurationAudit WHERE NewVersion=1"));
        }
        finally { Cleanup(path); }
    }

    [Fact(DisplayName = "TD-INF-005 workflow version capture preserves legacy null and explicit versions")]
    public async Task Workflow_version_capture_is_explicit_and_nullable()
    {
        var path = TempPath();
        try
        {
            var repository = new SqliteWorkflowConfigurationRepository(path);
            Assert.Null(await repository.GetWorkflowVersionAsync("legacy"));
            await repository.CaptureWorkflowVersionAsync("legacy", null);
            await repository.CaptureWorkflowVersionAsync("active", 7);
            Assert.Null(await repository.GetWorkflowVersionAsync("legacy"));
            Assert.Equal(7, await repository.GetWorkflowVersionAsync("active"));
        }
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
