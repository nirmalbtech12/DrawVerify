using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteWorkflowRepositoryTests
{
    [Fact(DisplayName = "TD-INFRA-01 atomic submission persists exact snapshot, status, and audit")]
    public async Task Submission_snapshot_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var package = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var snapshot = Snapshot(package); var state = new WorkflowState { PackageId = package, Status = WorkflowStatus.InPreparation, Revision = 1 };
            await repository.SubmitAtomicallyAsync(state, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            Assert.Equal(WorkflowStatus.SubmittedForReview, (await repository.GetStateAsync(package))!.Status);
            var loaded = Assert.Single(await repository.GetSubmissionsAsync(package));
            Assert.Equal(snapshot, loaded); Assert.Equal(DateTimeKind.Utc, loaded.SubmittedUtc.Kind);
            var queue = Assert.Single(await repository.GetCheckerQueueAsync());
            Assert.Equal(package, queue.PackageId); Assert.Equal(1, queue.Revision); Assert.Equal("Maker One", queue.MakerDisplayName);
            Assert.Equal(snapshot.VerificationEvidenceJson, queue.VerificationSummary);
            Assert.Equal(snapshot.DxfDocumentId, loaded.DxfDocumentId);
            Assert.Equal(snapshot.Note, loaded.Note);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INFRA-02 duplicate submission and failed transaction preserve state")]
    public async Task Duplicate_and_failed_transactions_are_rejected_atomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var state = new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }; var snapshot = Snapshot(package);
            await repository.SubmitAtomicallyAsync(state, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SubmitAtomicallyAsync(state, snapshot with { SubmissionId = Guid.NewGuid() }, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted")));
            Assert.Equal(WorkflowStatus.SubmittedForReview, (await repository.GetStateAsync(package))!.Status); Assert.Single(await repository.GetSubmissionsAsync(package));
            var newPackage = Guid.NewGuid(); var failing = Snapshot(newPackage) with { SubmissionId = snapshot.SubmissionId };
            await Assert.ThrowsAsync<SqliteException>(() => repository.SubmitAtomicallyAsync(state with { PackageId = newPackage }, failing, Audit(DocumentAuditAction.SubmissionSucceeded, newPackage, "Submitted")));
            Assert.Null(await repository.GetStateAsync(newPackage)); Assert.Empty(await repository.GetSubmissionsAsync(newPackage));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INFRA-02 superseding a submitted snapshot preserves its source payload")]
    public async Task Superseded_snapshot_remains_available_and_is_removed_from_queue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var package = Guid.NewGuid();
            var snapshot = Snapshot(package);
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));

            await repository.MarkSupersededAsync(snapshot.SubmissionId, Audit(DocumentAuditAction.SubmissionSuperseded, package, "Superseded"));

            var loaded = Assert.Single(await repository.GetSubmissionsAsync(package));
            Assert.Equal(WorkflowStatus.Superseded, loaded.Status);
            Assert.Equal(snapshot.ExtractionEvidenceJson, loaded.ExtractionEvidenceJson);
            Assert.Equal(snapshot.VerificationEvidenceJson, loaded.VerificationEvidenceJson);
            Assert.Empty(await repository.GetCheckerQueueAsync());
            Assert.Equal(WorkflowStatus.Superseded, (await repository.GetStateAsync(package))!.Status);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INFRA-03 submit, cancel, and invalid audit rows contain safe context")]
    public async Task Audit_rows_round_trip_with_utc_timestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid();
            foreach (var action in new[] { DocumentAuditAction.SubmissionSucceeded, DocumentAuditAction.SubmissionCancelled, DocumentAuditAction.SubmissionRejected }) await repository.RecordAuditAsync(Audit(action, package, action.ToString()));
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT Action,DocumentId,TimestampUtc,DetailsJson,UserId FROM DocumentAuditLog ORDER BY TimestampUtc";
            await using var reader = await command.ExecuteReaderAsync(); var actions = new List<DocumentAuditAction>(); while (await reader.ReadAsync()) { actions.Add((DocumentAuditAction)reader.GetInt32(0)); Assert.Equal(package.ToString(), reader.GetString(1)); Assert.Equal(DateTimeKind.Utc, DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Kind); Assert.Contains(package.ToString(), reader.GetString(3)); Assert.Equal("maker-001", reader.GetString(4)); Assert.Contains("revision", reader.GetString(3)); }
            Assert.Equal(3, actions.Count); Assert.Contains(DocumentAuditAction.SubmissionCancelled, actions); Assert.Contains(DocumentAuditAction.SubmissionRejected, actions); Assert.Empty(await repository.GetSubmissionsAsync(package));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INFRA-02 additive migration preserves legacy audit rows")]
    public async Task Migration_preserves_existing_audit_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-workflow-{Guid.NewGuid():N}.db");
        var package = Guid.NewGuid();
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); INSERT INTO DocumentAuditLog VALUES ('legacy-audit', $document, 0, 'legacy-user', '2026-01-01T00:00:00.0000000Z', $details);";
                command.Parameters.AddWithValue("$document", package.ToString());
                command.Parameters.AddWithValue("$details", "{\"legacy\":true}");
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SqliteWorkflowRepository(path);
            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await verify.OpenAsync();
            await using var count = verify.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog WHERE AuditId='legacy-audit'";
            Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally { Delete(path); }
    }

    private static ReviewSubmission Snapshot(Guid package) => new() { SubmissionId = Guid.NewGuid(), PackageId = package, DxfDocumentId = Guid.Parse("00000000-0000-0000-0000-000000000010"), StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"), VerificationSessionId = Guid.Parse("00000000-0000-0000-0000-000000000013"), ExtractionEvidenceJson = "{\"extracted\":true}", VerificationEvidenceJson = "{\"verified\":true}", MakerUserId = "maker-001", MakerDisplayName = "Maker One", SubmittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Note = "Please review hole tolerances", Revision = 1 };
    private static AuditEntry Audit(DocumentAuditAction action, Guid package, string result) => new() { DocumentId = package, UserId = "maker-001", Action = action, TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DetailsJson = $"{{\"packageId\":\"{package}\",\"revision\":1,\"result\":\"{result}\",\"maker\":\"Maker One\"}}" };
    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
