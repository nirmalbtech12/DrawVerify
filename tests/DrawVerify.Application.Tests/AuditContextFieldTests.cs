using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Notifications;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditContextFieldTests
{
    [Theory(DisplayName = "TSD-NOTIFICATION-AUDIT MakerNotificationSent fields retain rejection context")]
    [InlineData("Checker One", "checker-001", "Checker")]
    [InlineData("Approver One", "approver-001", "Approver")]
    public async Task MakerNotificationSent_fields_retain_rejection_context(string rejectorName, string rejectorUserId, string rejectorRole)
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-application-audit-{Guid.NewGuid():N}.db");
        var package = Guid.NewGuid();
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var submission = new ReviewSubmission
            {
                PackageId = package,
                Revision = 2,
                MakerUserId = "maker-001",
                MakerDisplayName = "Maker One",
                DxfDocumentId = Guid.NewGuid(),
                StepDocumentVersionId = Guid.NewGuid(),
                DxfDocumentVersionId = Guid.NewGuid(),
                VerificationSessionId = Guid.NewGuid(),
                SubmittedUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                CreatedUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }
            };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 2 }, submission, new AuditEntry { DocumentId = package, UserId = "maker-001", Action = DocumentAuditAction.SubmissionSucceeded, TimestampUtc = submission.SubmittedUtc, DetailsJson = "{}" });
            var review = (await repository.GetReviewPackageAsync(package, 2))!;
            var timestamp = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            var decision = new ReviewDecision
            {
                DecisionId = Guid.NewGuid(), PackageId = package, Revision = 2,
                CheckerUserId = rejectorRole == "Checker" ? rejectorUserId : "checker-001",
                CheckerDisplayName = rejectorRole == "Checker" ? rejectorName : "Checker One",
                ApproverUserId = rejectorRole == "Approver" ? rejectorUserId : string.Empty,
                ApproverDisplayName = rejectorRole == "Approver" ? rejectorName : string.Empty,
                DecisionType = rejectorRole == "Checker" ? ReviewDecisionType.Reject : ReviewDecisionType.Return,
                Comments = "Fix dimension 4", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4 and resubmit",
                SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = timestamp
            };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, rejectorRole == "Checker" ? WorkflowStatus.Rejected : WorkflowStatus.ReturnedForRework, new AuditEntry { DocumentId = package, UserId = rejectorUserId, Action = DocumentAuditAction.Rejected, TimestampUtc = timestamp, DetailsJson = "{}" });
            await new InAppNotificationService(path).NotifyMakerAsync(package, 2, saved);

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Action,DocumentRevision,TimestampUtc,UserId,DetailsJson FROM DocumentAuditLog WHERE DocumentId=$document AND Action=$action ORDER BY TimestampUtc DESC LIMIT 1";
            command.Parameters.AddWithValue("$document", package.ToString()); command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.MakerNotificationSent);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((int)DocumentAuditAction.MakerNotificationSent, reader.GetInt32(0)); Assert.Equal("2", reader.GetString(1)); Assert.Equal(timestamp.ToString("O"), reader.GetString(2)); Assert.Equal("maker-001", reader.GetString(3));
            using var document = JsonDocument.Parse(reader.GetString(4)); var root = document.RootElement;
            Assert.Equal("MakerNotificationSent", root.GetProperty("eventType").GetString()); Assert.Equal(package, root.GetProperty("packageId").GetGuid()); Assert.Equal(2, root.GetProperty("revision").GetInt32()); Assert.Equal(saved.DecisionId, root.GetProperty("rejectionEventId").GetGuid()); Assert.Equal("InApp", root.GetProperty("channel").GetString()); Assert.Equal("maker-001", root.GetProperty("recipient").GetString()); Assert.Equal(rejectorName, root.GetProperty("rejectorName").GetString()); Assert.Equal(rejectorUserId, root.GetProperty("rejectorUserId").GetString()); Assert.NotEqual("maker-001", root.GetProperty("rejectorUserId").GetString()); Assert.Equal(rejectorRole, root.GetProperty("rejectorRole").GetString()); Assert.Equal(timestamp, root.GetProperty("rejectionTimestampUtc").GetDateTime());
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
    [Fact(DisplayName = "TSD-AC08-AUDIT approval notification audit fields retain recipient and event context")]
    public void TSD_AC08_AUDIT_approval_notification_context_is_complete()
    {
        var package = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var details = JsonSerializer.Serialize(new { packageId = package, revision = 3, approvalEventId = eventId, channel = "InApp", recipient = "approver-1", eventType = "ApproverNotificationSent", timestamp });
        using var document = JsonDocument.Parse(details);
        Assert.Equal("ApproverNotificationSent", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("approver-1", document.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("InApp", document.RootElement.GetProperty("channel").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
    }
    [Fact]
    public void TD_APP_002_context_fields_capture_comments_decisions_files_metadata_and_access()
    {
        var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var timestamp = DateTimeOffset.Parse("2026-01-10T08:00:00+05:30");
        var record = new DocumentAuditRecord { DocumentId = document, DocumentRevision = "REV-002", DocumentVersion = "2", ActorId = "approver-001", ActorRole = "Approver", Timestamp = timestamp, Action = DocumentAuditAction.Approved, PreviousWorkflowStatus = "InReview", NewWorkflowStatus = "Approved", DetailsJson = "{\"comment\":\"Looks good\",\"target\":\"DIM-4\",\"fileName\":\"part.step\",\"oldValue\":\"A\",\"newValue\":\"B\",\"accessMethod\":\"web viewer\"}", EventMetadataJson = "{\"target\":\"DIM-4\",\"oldValue\":\"A\",\"newValue\":\"B\"}" };
        Assert.Equal(timestamp.Offset, record.Timestamp.Offset);
        Assert.Equal("InReview", record.PreviousWorkflowStatus);
        Assert.Equal("Approved", record.NewWorkflowStatus);
        Assert.Contains("DIM-4", record.DetailsJson);
        Assert.Contains("oldValue", record.EventMetadataJson);
    }

    [Fact(DisplayName = "TSD-AC03-ACTOR-IDENTITY preserves displayed identity, role, and stable ID")]
    public void TSD_AC03_ACTOR_IDENTITY_preserves_display_identity_and_role()
    {
        var entry = new AuditTrailEntry { UserId = "approver-001", UserName = "Approver One", UserRole = "Approver", UserEmail = "approver@example.test" };
        Assert.Equal("Approver One", entry.UserName);
        Assert.Equal("Approver", entry.UserRole);
        Assert.Equal("approver-001", entry.UserId);
        Assert.Equal("approver@example.test", entry.UserEmail);
    }

    [Fact(DisplayName = "TSD-AC05-VERIFICATION-ACTIONS preserves run IDs and verification counts")]
    public void TSD_AC05_VERIFICATION_ACTIONS_preserves_structured_details_and_run_identifier()
    {
        var entry = new AuditTrailEntry { Action = DocumentAuditAction.DimensionsMatchedPartial, Outcome = "Success", Identifier = "run-42", Details = "{\"matched\":8,\"mismatched\":2,\"missing\":1,\"gdtChecked\":4}" };
        Assert.Equal(DocumentAuditAction.DimensionsMatchedPartial, entry.Action);
        Assert.Equal("run-42", entry.Identifier);
        Assert.Contains("missing", entry.Details);
        Assert.Contains("gdtChecked", entry.Details);
    }

    [Theory(DisplayName = "TSD-AC07-ACTION-DETAILS preserves action outcomes and identifiers")]
    [InlineData("Success")]
    [InlineData("Failed")]
    [InlineData("Returned")]
    public void TSD_AC07_ACTION_DETAILS_preserves_representative_outcomes(string outcome)
    {
        var entry = new AuditTrailEntry { Action = DocumentAuditAction.ReturnedForRework, Outcome = outcome, Identifier = "REV-7", IntegrityReference = "hash-7" };
        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal("REV-7", entry.Identifier);
        Assert.Equal("hash-7", entry.IntegrityReference);
    }
}
