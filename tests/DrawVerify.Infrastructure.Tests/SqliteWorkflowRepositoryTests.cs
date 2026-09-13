using System.Globalization;
using System.Text.Json;
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

    [Fact(DisplayName = "TD-INF-049 review snapshot and audit fields round trip")]
    public async Task Review_snapshot_decision_and_audit_round_trip_exactly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-review-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var package = Guid.NewGuid(); var submission = Snapshot(package) with { Revision = 7 };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 7 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var loaded = Assert.Single(await repository.GetReviewPackageAsync(package, 7) is { } value ? new[] { value } : Array.Empty<ReviewPackage>());
            var decision = new ReviewDecision { PackageId = package, Revision = 7, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecidedUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), DecisionType = ReviewDecisionType.Reject, Comments = "fix dimensions", SnapshotReference = loaded.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(loaded) };
            var audit = new AuditEntry { DocumentId = package, UserId = "checker-1", Action = DocumentAuditAction.Rejected, TimestampUtc = decision.DecidedUtc, DetailsJson = "{\"packageId\":\"" + package + "\",\"revision\":7,\"snapshotReference\":\"" + loaded.SnapshotReference + "\",\"comment\":\"fix dimensions\"}" };
            await repository.SaveReviewDecisionAsync(loaded, decision, WorkflowStatus.Rejected, audit);
            var reopened = await repository.GetReviewPackageAsync(package, 7);
            Assert.Equal(WorkflowStatus.Rejected, reopened!.Submission.Status);
            Assert.Equal(decision.SnapshotJson, reopened.PriorDecision!.SnapshotJson);
            Assert.Equal("checker-1", reopened.PriorDecision.CheckerUserId);
            Assert.Equal("fix dimensions", reopened.PriorDecision.Comments);
            Assert.Equal(submission.ExtractionEvidenceJson, JsonSerializer.Deserialize<ReviewPackage>(reopened.PriorDecision.SnapshotJson)!.ExtractionEvidenceJson);
            await using var schemaConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await schemaConnection.OpenAsync();
            await using var schemaCommand = schemaConnection.CreateCommand();
            schemaCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('ReviewDecision','ReviewComment','InAppNotification')";
            Assert.Equal(3L, await schemaCommand.ExecuteScalarAsync());
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-050 stale and duplicate decisions fail atomically")]
    public async Task Duplicate_decision_does_not_mutate_state_or_create_second_row()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-review-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package);
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!;
            var first = new ReviewDecision { PackageId = package, Revision = 1, CheckerUserId = "checker-1", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecisionType = ReviewDecisionType.Approve };
            await repository.SaveReviewDecisionAsync(review, first, WorkflowStatus.Approved, Audit(DocumentAuditAction.Approved, package, "Approve"));
            var second = first with { DecisionId = Guid.NewGuid(), DecisionType = ReviewDecisionType.Reject, Comments = "too late" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveReviewDecisionAsync(review, second, WorkflowStatus.Rejected, Audit(DocumentAuditAction.Rejected, package, "Reject")));
            Assert.Equal(WorkflowStatus.Approved, (await repository.GetStateAsync(package))!.Status);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM ReviewDecision WHERE PackageId=$package AND Revision=1"; command.Parameters.AddWithValue("$package", package.ToString()); Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-051 successful review decision creates Maker notification")]
    public async Task Approved_decision_creates_durable_maker_notification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-review-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package);
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!; var decision = new ReviewDecision { PackageId = package, Revision = 1, CheckerUserId = "checker-1", DecisionType = ReviewDecisionType.Approve, SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review) };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, WorkflowStatus.Approved, Audit(DocumentAuditAction.Approved, package, "Approve"));
            await new DrawVerify.Infrastructure.Notifications.InAppNotificationService(path).NotifyMakerAsync(package, 1, saved);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT UserId,Message FROM InAppNotification WHERE PackageId=$package"; command.Parameters.AddWithValue("$package", package.ToString()); await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal("maker-001", reader.GetString(0)); Assert.Contains("Approve", reader.GetString(1));
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
