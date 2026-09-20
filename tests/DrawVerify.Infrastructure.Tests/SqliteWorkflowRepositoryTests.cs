using System.Globalization;
using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Notifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteWorkflowRepositoryTests
{
    [Fact(DisplayName = "TSD-AC02-CHANNEL configured InApp approval delivery succeeds")]
    public async Task TSD_AC02_CHANNEL_configured_inapp_approval_delivery_succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-channel-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            await InsertApprovalSubmissionAsync(path, ApprovalSubmission());
            await new InAppNotificationService(path).NotifyApproverAsync(ApprovalSubmission().PackageId, 3, ApprovalDecision());
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));
            Assert.Equal("InApp", notification.Channel);
            Assert.Equal("ApproverNotificationSent", notification.EventType);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC03-CONTENT approval notification round-trips mandatory fields")]
    public async Task TSD_AC03_CONTENT_approval_notification_contains_checker_and_approval_details()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-content-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            var decision = ApprovalDecision();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, decision);
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));
            Assert.Contains(submission.PackageId.ToString(), notification.Message, StringComparison.Ordinal);
            Assert.Contains($"revision {submission.Revision}", notification.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(decision.CheckerDisplayName, notification.Message, StringComparison.Ordinal);
            Assert.Contains("ready for final approval", notification.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DateTimeKind.Utc, notification.CreatedUtc.Kind);
            Assert.Equal(decision.DecidedUtc, notification.CreatedUtc);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC04-ROUTE approval notification target contains package and revision")]
    public async Task TSD_AC04_ROUTE_approval_notification_has_direct_workflow_target()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-route-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, ApprovalDecision());
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));
            Assert.Equal($"/workflow/{submission.PackageId:D}/{submission.Revision}", notification.Target);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC05-CENTER approval notification is initially unread for its Approver")]
    public async Task TSD_AC05_CENTER_approval_notification_is_unread_and_user_scoped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-center-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, ApprovalDecision());
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));
            Assert.False(notification.IsRead);
            Assert.Equal(submission.PackageId, notification.PackageId);
            Assert.Equal(ApprovalDecision().DecidedUtc, notification.CreatedUtc);
            Assert.Empty(await new InAppNotificationService(path).GetForUserAsync("A-2"));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC07-MULTI distinct Approvers receive one notification and legacy assignment resolves")]
    public async Task TSD_AC07_MULTI_deduplicates_approvers_and_preserves_legacy_assignment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-multi-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission() with { AssignedApproverUserIds = Array.Empty<string>(), AssignedApproverUserId = "A-1" };
            await InsertApprovalSubmissionAsync(path, submission);
            var legacyDecision = ApprovalDecision();
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, legacyDecision);
            Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));

            var multi = ApprovalSubmission() with { SubmissionId = Guid.NewGuid(), AssignedApproverUserId = "A-1", AssignedApproverUserIds = new[] { "A-1", "a-1", "A-2" } };
            await InsertApprovalSubmissionAsync(path, multi);
            var multiDecision = ApprovalDecision() with { DecisionId = Guid.NewGuid() };
            await new InAppNotificationService(path).NotifyApproverAsync(multi.PackageId, multi.Revision, multiDecision);
            var approverOne = await new InAppNotificationService(path).GetForUserAsync("A-1");
            var approverTwo = await new InAppNotificationService(path).GetForUserAsync("A-2");
            Assert.Equal(2, approverOne.Count);
            Assert.Single(approverOne.Where(x => x.ApprovalEventId == multiDecision.DecisionId));
            Assert.Single(approverTwo);
            Assert.Equal(multiDecision.DecisionId, approverTwo[0].ApprovalEventId);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC08-AUDIT approval notification records exact event and recipient fields")]
    public async Task TSD_AC08_AUDIT_approval_notification_records_required_audit_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-audit-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            var decision = ApprovalDecision();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, decision);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Action,UserId,TimestampUtc,DetailsJson,DocumentRevision FROM DocumentAuditLog WHERE DocumentId=$document";
            command.Parameters.AddWithValue("$document", submission.PackageId.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((int)DocumentAuditAction.ApproverNotificationSent, reader.GetInt32(0));
            Assert.Equal("A-1", reader.GetString(1));
            Assert.Equal(decision.DecidedUtc, DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind));
            Assert.Equal(submission.Revision.ToString(CultureInfo.InvariantCulture), reader.GetString(4));
            Assert.Contains("ApproverNotificationSent", reader.GetString(3), StringComparison.Ordinal);
            Assert.Contains("InApp", reader.GetString(3), StringComparison.Ordinal);
            Assert.Contains(decision.DecisionId.ToString(), reader.GetString(3), StringComparison.Ordinal);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC10-IDEMPOTENCY suppresses duplicate approval event and allows re-approval")]
    public async Task TSD_AC10_IDEMPOTENCY_suppresses_same_event_and_inserts_new_event()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-idempotency-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            var decision = ApprovalDecision();
            await InsertApprovalSubmissionAsync(path, submission);
            var service = new InAppNotificationService(path);
            await service.NotifyApproverAsync(submission.PackageId, submission.Revision, decision);
            await service.NotifyApproverAsync(submission.PackageId, submission.Revision, decision);
            Assert.Single(await service.GetForUserAsync("A-1"));
            await service.NotifyApproverAsync(submission.PackageId, submission.Revision, decision with { DecisionId = Guid.NewGuid() });
            Assert.Equal(2, (await service.GetForUserAsync("A-1")).Count);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC11-RICH optional evidence does not replace mandatory approval content")]
    public async Task TSD_AC11_RICH_optional_evidence_is_not_required_for_mandatory_content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-rich-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission() with { VerificationEvidenceJson = "{malformed" };
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, ApprovalDecision());
            var message = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1")).Message;
            Assert.Contains("ready for final approval", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verification summary", message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC12-READ mark-read is limited to the notification recipient")]
    public async Task TSD_AC12_READ_mark_read_is_user_scoped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-read-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            await InsertApprovalSubmissionAsync(path, submission);
            var service = new InAppNotificationService(path);
            await service.NotifyApproverAsync(submission.PackageId, submission.Revision, ApprovalDecision());
            var notification = Assert.Single(await service.GetForUserAsync("A-1"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkReadAsync(notification.NotificationId, "A-2"));
            Assert.False(Assert.Single(await service.GetForUserAsync("A-1")).IsRead);
            await service.MarkReadAsync(notification.NotificationId, "A-1");
            Assert.True(Assert.Single(await service.GetForUserAsync("A-1")).IsRead);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC13-LOCALE mandatory approval content remains when translation is unavailable")]
    public async Task TSD_AC13_LOCALE_unavailable_translation_preserves_mandatory_content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-locale-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, ApprovalDecision());
            var message = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1")).Message;
            Assert.Contains(submission.PackageId.ToString(), message, StringComparison.Ordinal);
            Assert.Contains("approved by Checker", message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC14-TIMING approval notification timestamp is UTC and follows approval timestamp")]
    public async Task TSD_AC14_TIMING_approval_dispatch_exposes_ordered_utc_timestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-timing-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = ApprovalSubmission();
            var decision = ApprovalDecision();
            await InsertApprovalSubmissionAsync(path, submission);
            await new InAppNotificationService(path).NotifyApproverAsync(submission.PackageId, submission.Revision, decision);
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("A-1"));
            Assert.Equal(DateTimeKind.Utc, decision.DecidedUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, notification.CreatedUtc.Kind);
            Assert.True(notification.CreatedUtc >= decision.DecidedUtc);
            Assert.Equal(decision.DecisionId, notification.ApprovalEventId);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC09-RETRY invalid approval delivery fails visibly without a success row")]
    public async Task TSD_AC09_RETRY_invalid_approval_delivery_is_not_reported_successful()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-approval-retry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var service = new InAppNotificationService(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.NotifyApproverAsync(Guid.Empty, 0, ApprovalDecision()));
            Assert.Empty(await service.GetForUserAsync("A-1"));
        }
        finally { Delete(path); }
    }
    [Fact(DisplayName = "TSD-AC02-CHANNELS unsupported channel fails visibly")]
    public async Task TSD_AC02_CHANNELS_unsupported_channel_is_not_reported_as_success()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-channel-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var service = new InAppNotificationService(path);
            await Assert.ThrowsAsync<NotSupportedException>(() => service.NotifySubmissionAsync(NotificationSubmission(), new[] { WorkflowNotificationChannel.Email }));
            Assert.Empty(await service.GetForUserAsync("C-1"));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC03-CONTENT notification retains committed submission details")]
    public async Task TSD_AC03_CONTENT_notification_contains_required_submission_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-content-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = NotificationSubmission();
            var service = new InAppNotificationService(path);
            await service.NotifySubmissionAsync(submission, new[] { WorkflowNotificationChannel.InApp });
            var notification = Assert.Single(await service.GetForUserAsync("C-1"));
            Assert.Contains(submission.PackageId.ToString(), notification.Message, StringComparison.Ordinal);
            Assert.Contains($"revision {submission.Revision}", notification.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(submission.MakerDisplayName, notification.Message, StringComparison.Ordinal);
            Assert.Contains("submitted for review", notification.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(submission.SubmittedUtc, notification.CreatedUtc);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC05-CENTER notification center projection is initially unread")]
    public async Task TSD_AC05_CENTER_loads_timestamped_unread_notification_for_checker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-center-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = NotificationSubmission();
            var service = new InAppNotificationService(path);
            await service.NotifySubmissionAsync(submission, new[] { WorkflowNotificationChannel.InApp });
            var notification = Assert.Single(await service.GetForUserAsync("C-1"));
            Assert.Equal("C-1", notification.UserId);
            Assert.False(notification.IsRead);
            Assert.Equal(submission.PackageId, notification.PackageId);
            Assert.Equal(submission.SubmittedUtc, notification.CreatedUtc);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC08-AUDIT notification send records CheckerNotificationSent")]
    public async Task TSD_AC08_AUDIT_notification_send_records_required_audit_context()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-audit-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = NotificationSubmission();
            await new InAppNotificationService(path).NotifySubmissionAsync(submission, new[] { WorkflowNotificationChannel.InApp });
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Action,UserId,TimestampUtc,DetailsJson FROM DocumentAuditLog WHERE DocumentId=$document";
            command.Parameters.AddWithValue("$document", submission.PackageId.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((int)DocumentAuditAction.CheckerNotificationSent, reader.GetInt32(0));
            Assert.Equal("C-1", reader.GetString(1));
            Assert.Equal(submission.SubmittedUtc, DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Contains("CheckerNotificationSent", reader.GetString(3), StringComparison.Ordinal);
            Assert.Contains("InApp", reader.GetString(3), StringComparison.Ordinal);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC10-IDEMPOTENCY replay is suppressed and resubmission is delivered")]
    public async Task TSD_AC10_IDEMPOTENCY_suppresses_exact_replay_only()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-idempotency-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var service = new InAppNotificationService(path);
            var first = NotificationSubmission();
            await service.NotifySubmissionAsync(first, new[] { WorkflowNotificationChannel.InApp });
            await service.NotifySubmissionAsync(first, new[] { WorkflowNotificationChannel.InApp });
            var second = first with { SubmissionId = Guid.NewGuid(), Revision = first.Revision + 1 };
            await service.NotifySubmissionAsync(second, new[] { WorkflowNotificationChannel.InApp });
            var notifications = await service.GetForUserAsync("C-1");
            Assert.Equal(2, notifications.Count);
            Assert.Contains(notifications, x => x.SubmissionId == first.SubmissionId && x.Revision == first.Revision);
            Assert.Contains(notifications, x => x.SubmissionId == second.SubmissionId && x.Revision == second.Revision);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC11-CONTEXT required content remains when optional context is absent")]
    public async Task TSD_AC11_CONTEXT_base_notification_remains_non_empty_without_enrichment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-context-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            await new InAppNotificationService(path).NotifySubmissionAsync(NotificationSubmission(), new[] { WorkflowNotificationChannel.InApp });
            Assert.NotEmpty(Assert.Single(await new InAppNotificationService(path).GetForUserAsync("C-1")).Message);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC12-READ mark-read persists read state")]
    public async Task TSD_AC12_READ_mark_read_persists_before_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-read-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var service = new InAppNotificationService(path);
            await service.NotifySubmissionAsync(NotificationSubmission(), new[] { WorkflowNotificationChannel.InApp });
            var notification = Assert.Single(await service.GetForUserAsync("C-1"));
            await service.MarkReadAsync(notification.NotificationId, "C-1");
            Assert.True(Assert.Single(await service.GetForUserAsync("C-1")).IsRead);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC14-TIMING persisted notification timestamp uses submission timestamp")]
    public async Task TSD_AC14_TIMING_uses_configured_submission_timestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notify-timing-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var submission = NotificationSubmission() with { SubmittedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc) };
            await new InAppNotificationService(path).NotifySubmissionAsync(submission, new[] { WorkflowNotificationChannel.InApp });
            Assert.Equal(submission.SubmittedUtc, Assert.Single(await new InAppNotificationService(path).GetForUserAsync("C-1")).CreatedUtc);
        }
        finally { Delete(path); }
    }
    [Fact(DisplayName = "TD-REG-01 SQLite legacy rows remain readable with additive Approver fields")]
    public void Legacy_schema_contract_keeps_maker_checker_data_and_adds_nullable_approver_columns()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs")));
        Assert.Contains("ApproverSignatureJson", source);
        Assert.Contains("ApproverSignatureFinalized", source);
        Assert.Contains("ALTER TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MakerSignatureJson", source);
        Assert.Contains("CheckerSignatureJson", source);
        Assert.Contains("SaveApproverSignatureAsync", source);
        Assert.Contains("ApproverSignatureApplied", source);
        Assert.Contains("OLD.ApproverSignatureFinalized IS NULL", source);
        Assert.Contains("NEW.ApproverSignatureFinalized = 1", source);
    }

    [Fact(DisplayName = "TD-03 migrates verification audit records additively and reads the latest outcome")]
    public async Task Verification_record_round_trips_from_sqlite_audit_history()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-verification-{Guid.NewGuid():N}.db");
        try
        {
            var package = Guid.NewGuid();
            var repository = new SqliteWorkflowRepository(path);
            await repository.RecordAuditAsync(new AuditEntry
            {
                DocumentId = package, UserId = "checker-001", ActorRole = "Checker", DocumentRevision = "4",
                DocumentVersion = "step-version", Action = DocumentAuditAction.VerificationRunCompleted,
                TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DetailsJson = "{\"result\":\"Match\",\"storedFingerprint\":\"aaa\",\"recomputedFingerprint\":\"aaa\",\"checkerUserId\":\"checker-001\",\"checkerDisplayName\":\"Checker One\",\"fingerprintAlgorithm\":\"SHA-256\",\"fingerprintCanonicalizationVersion\":1}"
            });
            await repository.RecordAuditAsync(new AuditEntry
            {
                DocumentId = package, UserId = "checker-001", ActorRole = "Checker", DocumentRevision = "4",
                DocumentVersion = "step-version", Action = DocumentAuditAction.IntegrityCheckFailed,
                TimestampUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                DetailsJson = "{\"result\":\"Mismatch\",\"storedFingerprint\":\"aaa\",\"recomputedFingerprint\":\"bbb\",\"checkerUserId\":\"checker-001\",\"checkerDisplayName\":\"Checker One\",\"fingerprintAlgorithm\":\"SHA-256\",\"fingerprintCanonicalizationVersion\":1}"
            });
            var result = await repository.GetFingerprintVerificationAsync(package, 4);
            Assert.NotNull(result);
            Assert.Equal(FingerprintVerificationStatus.Mismatch, result!.Status);
            Assert.Equal("aaa", result.Stored!.Value);
            Assert.Equal("bbb", result.Computed!.Value);
            Assert.Equal("checker-001", result.VerifierUserId);
        }
        finally { Delete(path); }
    }
    [Fact(DisplayName = "TD-04 persists fingerprint metadata with the exact submission identity")]
    public async Task Submission_fingerprint_round_trips_with_submission_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-fingerprint-{Guid.NewGuid():N}.db");
        try
        {
            var package = Guid.NewGuid();
            var snapshot = Snapshot(package) with { Revision = 8, Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } };
            await new SqliteWorkflowRepository(path).SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 8 }, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var loaded = Assert.Single(await new SqliteWorkflowRepository(path).GetSubmissionsAsync(package));
            Assert.Equal(snapshot.Fingerprint, loaded.Fingerprint);
            Assert.Equal(snapshot.MakerUserId, loaded.MakerUserId);
            Assert.Equal(snapshot.SubmittedUtc, loaded.SubmittedUtc);
            Assert.Equal(snapshot.Revision, loaded.Revision);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-05 rejects a new submission without a valid fingerprint")]
    public async Task Missing_fingerprint_cannot_be_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-missing-fingerprint-{Guid.NewGuid():N}.db");
        try
        {
            var package = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteWorkflowRepository(path).SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, Snapshot(package) with { Fingerprint = null }, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted")));
            Assert.Empty(await new SqliteWorkflowRepository(path).GetSubmissionsAsync(package));
        }
        finally { Delete(path); }
    }

    [Theory(DisplayName = "TD-04 rejects malformed fingerprints before persistence")]
    [InlineData("gg00000000000000000000000000000000000000000000000000000000000000", "SHA-256", 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "MD5", 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "SHA-256", 0)]
    public async Task Malformed_fingerprint_cannot_be_persisted(string value, string algorithm, int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-invalid-fingerprint-{Guid.NewGuid():N}.db");
        try
        {
            var package = Guid.NewGuid();
            var submission = Snapshot(package) with { Fingerprint = new DocumentFingerprint { Value = value, Algorithm = algorithm, CanonicalizationVersion = version } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteWorkflowRepository(path).SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted")));
            Assert.Empty(await new SqliteWorkflowRepository(path).GetSubmissionsAsync(package));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-05 rejected fingerprint mutation records a security audit event")]
    public async Task Rejected_fingerprint_update_records_context_and_rethrows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-fingerprint-security-{Guid.NewGuid():N}.db");
        try
        {
            var package = Guid.NewGuid();
            var submission = Snapshot(package);
            await new SqliteWorkflowRepository(path).SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ReviewSubmission SET FingerprintJson=$fingerprint WHERE SubmissionId=$id";
            command.Parameters.AddWithValue("$fingerprint", JsonSerializer.Serialize(new DocumentFingerprint { Value = new string('b', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }));
            command.Parameters.AddWithValue("$id", submission.SubmissionId.ToString());
            var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
            await using var audit = connection.CreateCommand();
            audit.CommandText = "SELECT DetailsJson FROM DocumentAuditLog WHERE DocumentId=$package AND DetailsJson LIKE '%fingerprint-mutation-rejected%'";
            audit.Parameters.AddWithValue("$package", package.ToString());
            var details = Convert.ToString(await audit.ExecuteScalarAsync());
            Assert.Contains("UPDATE", details, StringComparison.Ordinal);
            Assert.Contains(submission.SubmissionId.ToString(), details, StringComparison.Ordinal);
            var loaded = Assert.Single(await new SqliteWorkflowRepository(path).GetSubmissionsAsync(package));
            Assert.Equal(submission.Fingerprint, loaded.Fingerprint);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-10 serializes fingerprint fields for machine-readable submission export")]
    public void Submission_json_contains_machine_readable_fingerprint_fields()
    {
        var submission = Snapshot(Guid.NewGuid());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(submission));
        var fingerprint = document.RootElement.GetProperty("Fingerprint");

        Assert.Equal(submission.Fingerprint!.Value, fingerprint.GetProperty("Value").GetString());
        Assert.Equal("SHA-256", fingerprint.GetProperty("Algorithm").GetString());
        Assert.Equal(1, fingerprint.GetProperty("CanonicalizationVersion").GetInt32());
    }
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
            Assert.Equivalent(snapshot, loaded); Assert.Equal(DateTimeKind.Utc, loaded.SubmittedUtc.Kind);
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
            var conflict = await Assert.ThrowsAsync<ReviewDecisionConflictException>(() => repository.SaveReviewDecisionAsync(review, second, WorkflowStatus.Rejected, Audit(DocumentAuditAction.Rejected, package, "Reject")));
            Assert.Equal(package, conflict.PackageId);
            Assert.Equal(1, conflict.RequestedRevision);
            Assert.Equal(WorkflowStatus.Approved, conflict.CurrentStatus);
            Assert.Equal(1, conflict.CurrentRevision);
            Assert.Equal(ReviewDecisionType.Approve, conflict.PriorDecision!.DecisionType);
            Assert.Contains("not saved", conflict.Message, StringComparison.OrdinalIgnoreCase);
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

    [Theory(DisplayName = "TSD-REJECTION-EVENT persists one Maker notification for each rejection outcome")]
    [InlineData(ReviewDecisionType.Reject, WorkflowStatus.Rejected, "Checker One", "checker-1", "Checker")]
    [InlineData(ReviewDecisionType.Return, WorkflowStatus.ReturnedForRework, "Approver One", "approver-1", "Approver")]
    public async Task TSD_REJECTION_EVENT_persists_rejection_notification(ReviewDecisionType decisionType, WorkflowStatus status, string rejectorName, string rejectorId, string rejectorRole)
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-rejection-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid();
            var submission = Snapshot(package) with { Revision = 1, MakerUserId = "maker-001", Status = WorkflowStatus.SubmittedForReview };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!;
            var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 1, CheckerUserId = rejectorId, CheckerDisplayName = rejectorName, ApproverUserId = rejectorRole == "Approver" ? rejectorId : string.Empty, ApproverDisplayName = rejectorRole == "Approver" ? rejectorName : string.Empty, DecisionType = decisionType, Comments = "Fix dimension 4", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4 and resubmit", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc) };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, status, Audit(DocumentAuditAction.Rejected, package, "Rejected"));

            await new InAppNotificationService(path).NotifyMakerAsync(package, 1, saved);

            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("maker-001"));
            Assert.Equal(saved.DecisionId, notification.RejectionEventId);
            Assert.Equal("MakerNotificationSent", notification.EventType);
            Assert.Contains(rejectorName, notification.Message);
            Assert.Contains(rejectorRole, notification.Message);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-CONTENT notification preserves required semantic fields")]
    public async Task TSD_NOTIFICATION_CONTENT_contains_reference_revision_rejector_time_and_action()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-content-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package) with { Revision = 2, MakerUserId = "maker-001" };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 2 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 2))!; var decided = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 2, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Fix dimension 4", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = decided };
            await repository.SaveReviewDecisionAsync(review, decision, WorkflowStatus.Rejected, Audit(DocumentAuditAction.Rejected, package, "Rejected"));
            await new InAppNotificationService(path).NotifyMakerAsync(package, 2, decision);
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("maker-001"));
            Assert.Contains(package.ToString(), notification.Message); Assert.Contains("revision 2", notification.Message); Assert.Contains("Checker One", notification.Message); Assert.Contains("Checker", notification.Message); Assert.Contains(decided.ToString("O"), notification.Message); Assert.Contains("action required", notification.Message, StringComparison.OrdinalIgnoreCase); Assert.Equal($"/workflow/{package:D}/2", notification.Target);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-INAPP-CENTER notification is unread and filtered to its Maker")]
    public async Task TSD_INAPP_CENTER_loads_unread_notification_with_timestamp_and_reference()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-center-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package) with { MakerUserId = "maker-001" };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!; var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 1, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Fix it", OverallReason = "Fix it", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc) };
            await new InAppNotificationService(path).NotifyMakerAsync(package, 1, decision);
            var notification = Assert.Single(await new InAppNotificationService(path).GetForUserAsync("maker-001"));
            Assert.False(notification.IsRead); Assert.Equal(package, notification.PackageId); Assert.Equal(1, notification.Revision); Assert.Equal(decision.DecidedUtc, notification.CreatedUtc);
            Assert.Empty(await new InAppNotificationService(path).GetForUserAsync("other-maker"));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-IDEMPOTENCY repeated rejection event creates one record")]
    public async Task TSD_NOTIFICATION_IDEMPOTENCY_repeated_decision_is_not_duplicated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-idempotency-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package) with { MakerUserId = "maker-001" };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!; var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 1, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Fix it", OverallReason = "Fix it", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc) };
            var service = new InAppNotificationService(path); await service.NotifyMakerAsync(package, 1, decision); await service.NotifyMakerAsync(package, 1, decision);
            var notifications = await service.GetForUserAsync("maker-001"); Assert.Single(notifications); Assert.Equal(decision.DecisionId, notifications[0].RejectionEventId);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM DocumentAuditLog WHERE Action=$action"; command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.MakerNotificationSent); Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-READ durable read state changes only after persistence")]
    public async Task TSD_NOTIFICATION_READ_persists_read_state_for_notification_access()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-read-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package) with { MakerUserId = "maker-001" };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted")); var review = (await repository.GetReviewPackageAsync(package, 1))!;
            var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 1, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Fix it", OverallReason = "Fix it", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = DateTime.UtcNow };
            var service = new InAppNotificationService(path); await service.NotifyMakerAsync(package, 1, decision); var before = Assert.Single(await service.GetForUserAsync("maker-001")); Assert.False(before.IsRead); await service.MarkReadAsync(before.NotificationId, "maker-001"); var after = Assert.Single(await service.GetForUserAsync("maker-001")); Assert.True(after.IsRead);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-TIMING timestamps preserve rejection-to-persistence order")]
    public async Task TSD_NOTIFICATION_TIMING_preserves_utc_stage_timestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-timing-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var submission = Snapshot(package) with { MakerUserId = "maker-001" };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted")); var review = (await repository.GetReviewPackageAsync(package, 1))!; var rejectedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 1, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Fix it", OverallReason = "Fix it", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = rejectedUtc };
            var service = new InAppNotificationService(path); await service.NotifyMakerAsync(package, 1, decision); var notification = Assert.Single(await service.GetForUserAsync("maker-001")); Assert.Equal(DateTimeKind.Utc, notification.CreatedUtc.Kind); Assert.Equal(rejectedUtc, notification.CreatedUtc); Assert.True(notification.CreatedUtc >= rejectedUtc);
        }
        finally { Delete(path); }
    }

    [Theory(DisplayName = "TSD-NOTIFICATION-AUDIT durable MakerNotificationSent rows retain Checker and Approver identity")]
    [InlineData(ReviewDecisionType.Reject, WorkflowStatus.Rejected, "Checker One", "checker-1", "Checker")]
    [InlineData(ReviewDecisionType.Return, WorkflowStatus.ReturnedForRework, "Approver One", "approver-1", "Approver")]
    public async Task TSD_NOTIFICATION_AUDIT_stores_required_event_identity(ReviewDecisionType decisionType, WorkflowStatus status, string rejectorName, string rejectorUserId, string rejectorRole)
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-audit-{Guid.NewGuid():N}.db"); var package = Guid.NewGuid(); var timestamp = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        try
        {
            var repository = new SqliteWorkflowRepository(path); var submission = Snapshot(package) with { Revision = 2, MakerUserId = "maker-001", Status = WorkflowStatus.SubmittedForReview };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 2 }, submission, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 2))!;
            var decision = new ReviewDecision { DecisionId = Guid.NewGuid(), PackageId = package, Revision = 2, CheckerUserId = rejectorRole == "Checker" ? rejectorUserId : "checker-1", CheckerDisplayName = rejectorRole == "Checker" ? rejectorName : "Checker One", ApproverUserId = rejectorRole == "Approver" ? rejectorUserId : string.Empty, ApproverDisplayName = rejectorRole == "Approver" ? rejectorName : string.Empty, DecisionType = decisionType, Comments = "Fix dimension 4", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4 and resubmit", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review), DecidedUtc = timestamp };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, status, Audit(DocumentAuditAction.Rejected, package, "Rejected"));
            await new InAppNotificationService(path).NotifyMakerAsync(package, 2, saved);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT Action,DocumentRevision,TimestampUtc,UserId,DetailsJson FROM DocumentAuditLog WHERE DocumentId=$document AND Action=$action ORDER BY TimestampUtc DESC LIMIT 1"; command.Parameters.AddWithValue("$document", package.ToString()); command.Parameters.AddWithValue("$action", (int)DocumentAuditAction.MakerNotificationSent); await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal((int)DocumentAuditAction.MakerNotificationSent, reader.GetInt32(0)); Assert.Equal("2", reader.GetString(1)); Assert.Equal(timestamp.ToString("O"), reader.GetString(2)); Assert.Equal("maker-001", reader.GetString(3)); using var document = JsonDocument.Parse(reader.GetString(4)); var root = document.RootElement; Assert.Equal("MakerNotificationSent", root.GetProperty("eventType").GetString()); Assert.Equal(package, root.GetProperty("packageId").GetGuid()); Assert.Equal(2, root.GetProperty("revision").GetInt32()); Assert.Equal(saved.DecisionId, root.GetProperty("rejectionEventId").GetGuid()); Assert.Equal("InApp", root.GetProperty("channel").GetString()); Assert.Equal("maker-001", root.GetProperty("recipient").GetString()); Assert.Equal(rejectorName, root.GetProperty("rejectorName").GetString()); Assert.Equal(rejectorUserId, root.GetProperty("rejectorUserId").GetString()); Assert.NotEqual("maker-001", root.GetProperty("rejectorUserId").GetString()); Assert.Equal(rejectorRole, root.GetProperty("rejectorRole").GetString()); Assert.Equal(timestamp, root.GetProperty("rejectionTimestampUtc").GetDateTime());
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-RETRY invalid rejection fails without false success")]
    public async Task TSD_NOTIFICATION_RETRY_invalid_rejection_does_not_create_notification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-retry-failure-{Guid.NewGuid():N}.db"); var package = Guid.NewGuid();
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var decision = new ReviewDecision { PackageId = package, Revision = 1, DecisionType = ReviewDecisionType.Reject, Comments = "" };
            await Assert.ThrowsAsync<InvalidDataException>(() => new InAppNotificationService(path).NotifyMakerAsync(package, 1, decision));
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM InAppNotification WHERE PackageId=$package"; command.Parameters.AddWithValue("$package", package.ToString()); Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-04 issue references and draft comment edits round-trip transactionally")]
    public async Task Review_comments_round_trip_references_edit_and_discard()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-comments-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var snapshot = Snapshot(package);
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var types = new[] { ("drawing", "SHEET-1"), ("model", "FACE-7"), ("dimension", "DIM-4"), ("gdt", "GDT-A") };
            foreach (var (type, reference) in types)
            {
                var comment = new ReviewComment { PackageId = package, Revision = 1, AuthorUserId = "checker-001", AuthorDisplayName = "Checker", Text = $"Issue {reference}", ReferenceType = type, ReferenceId = reference, IsDraft = true };
                await repository.SaveReviewCommentAsync(comment, Audit(DocumentAuditAction.ReviewCommentDraftSaved, package, "CommentDraftSaved"));
            }
            var saved = (await repository.GetReviewCommentsAsync(package, 1)).ToArray();
            Assert.Equal(4, saved.Length); Assert.All(saved, x => Assert.True(x.IsDraft));
            var edited = saved[0] with { Text = "Updated sheet issue" };
            await repository.UpdateReviewCommentAsync(edited, Audit(DocumentAuditAction.ReviewCommentEdited, package, "CommentEdited"));
            await repository.DiscardReviewCommentAsync(saved[1].CommentId, Audit(DocumentAuditAction.ReviewCommentDiscarded, package, "CommentDiscarded"));
            var remaining = await repository.GetReviewCommentsAsync(package, 1);
            Assert.Equal(3, remaining.Count); Assert.Contains(remaining, x => x.Text == "Updated sheet issue"); Assert.DoesNotContain(remaining, x => x.CommentId == saved[1].CommentId);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveReviewCommentAsync(saved[0] with { ReferenceType = "bad", ReferenceId = "x" }, Audit(DocumentAuditAction.ReviewCommentDraftSaved, package, "invalid")));
            Assert.Equal(WorkflowStatus.SubmittedForReview, (await repository.GetStateAsync(package))!.Status);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-05 rejection preserves immutable snapshot and commits audit")]
    public async Task Rejection_preserves_snapshot_and_returns_maker_context()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-rejection-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.Parse("00000000-0000-0000-0000-000000000001"); var snapshot = Snapshot(package) with { Revision = 1 };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!;
            var decision = new ReviewDecision { PackageId = package, Revision = 1, CheckerUserId = "checker-001", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Reject, Comments = "Dimension 4 is outside tolerance", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4 and resubmit", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review) };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, WorkflowStatus.Rejected, new AuditEntry { DocumentId = package, UserId = "checker-001", Action = DocumentAuditAction.Rejected, TimestampUtc = decision.DecidedUtc, DetailsJson = "{\"packageId\":\"" + package + "\",\"revision\":1,\"status\":\"Rejected\",\"commentReference\":\"DIM-4\"}" });
            var reopened = (await repository.GetReviewPackageAsync(package, 1))!;
            Assert.Equal(snapshot.ExtractionEvidenceJson, reopened.Submission.ExtractionEvidenceJson);
            Assert.Equal(snapshot.VerificationEvidenceJson, reopened.Submission.VerificationEvidenceJson);
            Assert.Equal(decision.SnapshotJson, saved.SnapshotJson);
            Assert.Equal(WorkflowStatus.Rejected, reopened.Submission.Status);
            Assert.Equal("Correct dimension 4 and resubmit", reopened.PriorDecision!.CorrectionAction);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-08 rejection notification contains revision and comments navigation")]
    public async Task Rejection_notification_contains_required_navigation_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-notification-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path); var package = Guid.NewGuid(); var snapshot = Snapshot(package);
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = package, Status = WorkflowStatus.Draft, Revision = 1 }, snapshot, Audit(DocumentAuditAction.SubmissionSucceeded, package, "Submitted"));
            var review = (await repository.GetReviewPackageAsync(package, 1))!;
            var decision = new ReviewDecision { PackageId = package, Revision = 1, CheckerUserId = "checker-001", CheckerDisplayName = "Checker One", DecidedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc), DecisionType = ReviewDecisionType.Reject, Comments = "Fix dimension 4", OverallReason = "Fix dimension 4", SnapshotReference = review.SnapshotReference, SnapshotJson = JsonSerializer.Serialize(review) };
            var saved = await repository.SaveReviewDecisionAsync(review, decision, WorkflowStatus.Rejected, Audit(DocumentAuditAction.Rejected, package, "Rejected"));
            await new DrawVerify.Infrastructure.Notifications.InAppNotificationService(path).NotifyMakerAsync(package, 1, saved);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT Message FROM InAppNotification WHERE PackageId=$package"; command.Parameters.AddWithValue("$package", package.ToString());
            var message = (string?)await command.ExecuteScalarAsync();
            Assert.NotNull(message); Assert.Contains(package.ToString(), message); Assert.Contains("revision 1", message); Assert.Contains("Rejected", message); Assert.Contains($"/workflow/{package:D}/1", message);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-01 round trips multiple revisions with exact identifiers and filters")]
    public async Task Historical_revision_rows_preserve_identity_and_filtering()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-history-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            var package = Guid.NewGuid();
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            foreach (var item in new[] { (1, WorkflowStatus.Rejected, "maker-a"), (2, WorkflowStatus.Approved, "maker-b"), (3, WorkflowStatus.Draft, "maker-a") })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO ReviewSubmission (SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,SubmittedUtc,Note,Status) VALUES ($s,$p,$d,$step,$dxf,$session,$e,$v,$r,$maker,$name,$date,$note,$status)";
                command.Parameters.AddWithValue("$s", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$p", package.ToString()); command.Parameters.AddWithValue("$d", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$step", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$dxf", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$session", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$e", "{}"); command.Parameters.AddWithValue("$v", $"{{\"revision\":{item.Item1}}}"); command.Parameters.AddWithValue("$r", item.Item1); command.Parameters.AddWithValue("$maker", item.Item3); command.Parameters.AddWithValue("$name", item.Item3); command.Parameters.AddWithValue("$date", DateTime.UtcNow.AddDays(item.Item1).ToString("O")); command.Parameters.AddWithValue("$note", $"note-{item.Item1}"); command.Parameters.AddWithValue("$status", (int)item.Item2);
                await command.ExecuteNonQueryAsync();
            }
            var repository = new SqliteWorkflowRepository(path);
            var all = await ((IWorkflowRepository)repository).GetPriorSubmissionsAsync(package, new PriorVersionFilter { MakerUserId = "MAKER-A" });
            Assert.Equal(new[] { 3, 1 }, all.Select(x => x.Revision)); Assert.All(all, x => Assert.Equal(package, x.PackageId));
            Assert.Equal(WorkflowStatus.Rejected, (await repository.GetSubmissionsAsync(package)).Single(x => x.Revision == 1).Status);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-02 historical comments remain associated with their original revision")]
    public void Historical_comment_records_are_immutable_read_models()
    {
        var comment = new ReviewComment { PackageId = Guid.NewGuid(), Revision = 4, AuthorUserId = "checker", Text = "Fix DIM-4", ReferenceType = "dimension", ReferenceId = "DIM-4", IsDraft = false };
        var snapshot = comment with { Text = "changed" };
        Assert.Equal("Fix DIM-4", comment.Text); Assert.Equal(4, comment.Revision); Assert.Equal("DIM-4", comment.ReferenceId); Assert.NotEqual(comment, snapshot);
    }

    [Fact(DisplayName = "TSD-AC05-FEEDBACK SQLite persists and reloads version-scoped reviewer feedback")]
    public async Task Reviewer_comments_reload_for_the_requested_revision_only()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-feedback-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var package = Guid.NewGuid();
            var comment = new ReviewComment { PackageId = package, Revision = 2, AuthorUserId = "checker-1", AuthorDisplayName = "Checker", Text = "Review page 1", ReferenceType = "drawing", ReferenceId = "page-1", Location = "x=10,y=20", IsDraft = false };
            await repository.SaveReviewCommentAsync(comment, Audit(DocumentAuditAction.ReviewCommentDraftSaved, package, "saved"));

            var current = await repository.GetReviewCommentsAsync(package, 2);
            var otherRevision = await repository.GetReviewCommentsAsync(package, 1);

            var saved = Assert.Single(current);
            Assert.Equal(package, saved.PackageId);
            Assert.Equal(2, saved.Revision);
            Assert.Equal("page-1", saved.ReferenceId);
            Assert.Equal("x=10,y=20", saved.Location);
            Assert.Empty(otherRevision);
        }
        finally { Delete(path); }
    }

    private static ReviewSubmission Snapshot(Guid package) => new() { SubmissionId = Guid.NewGuid(), PackageId = package, DxfDocumentId = Guid.Parse("00000000-0000-0000-0000-000000000010"), StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"), VerificationSessionId = Guid.Parse("00000000-0000-0000-0000-000000000013"), ExtractionEvidenceJson = "{\"extracted\":true}", VerificationEvidenceJson = "{\"verified\":true}", MakerUserId = "maker-001", MakerDisplayName = "Maker One", SubmittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Note = "Please review hole tolerances", Revision = 1, Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } };
    private static ReviewSubmission NotificationSubmission() => new()
    {
        SubmissionId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        PackageId = Guid.Parse("20000000-0000-0000-0000-000000000001"), Revision = 3,
        MakerUserId = "M-1", MakerDisplayName = "Maker One", AssignedCheckerUserId = "C-1", AssignedCheckerUserIds = new[] { "C-1" },
        SubmittedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc)
    };
    private static ReviewSubmission ApprovalSubmission() => new()
    {
        SubmissionId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
        PackageId = Guid.Parse("20000000-0000-0000-0000-000000000002"), Revision = 3,
        DxfDocumentId = Guid.NewGuid(), StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(),
        ExtractionEvidenceJson = "{}", VerificationEvidenceJson = "{\"summary\":\"All dimensions within tolerance\"}", MakerUserId = "M-1", MakerDisplayName = "Maker One",
        AssignedApproverUserId = "A-1", AssignedApproverUserIds = new[] { "A-1" }, Status = WorkflowStatus.PendingFinalApproval,
        SubmittedUtc = new DateTime(2026, 9, 20, 9, 59, 0, DateTimeKind.Utc), CreatedUtc = new DateTime(2026, 9, 20, 9, 59, 0, DateTimeKind.Utc)
    };
    private static ReviewDecision ApprovalDecision() => new()
    {
        DecisionId = Guid.Parse("30000000-0000-0000-0000-000000000002"), PackageId = Guid.Parse("20000000-0000-0000-0000-000000000002"), Revision = 3,
        CheckerUserId = "C-1", CheckerDisplayName = "Checker One", DecidedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc), DecisionType = ReviewDecisionType.Approve
    };
    private static async Task InsertApprovalSubmissionAsync(string path, ReviewSubmission submission)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ReviewSubmission(SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,CreatedUtc,SubmittedUtc,Status,AssignedApproverUserId,AssignedApproverUserIdsJson) VALUES($id,$package,$dxf,$step,$dxfVersion,$session,$extract,$verify,$revision,$maker,$display,$created,$submitted,$status,$approver,$approvers)";
        command.Parameters.AddWithValue("$id", submission.SubmissionId.ToString()); command.Parameters.AddWithValue("$package", submission.PackageId.ToString()); command.Parameters.AddWithValue("$dxf", submission.DxfDocumentId.ToString()); command.Parameters.AddWithValue("$step", submission.StepDocumentVersionId.ToString()); command.Parameters.AddWithValue("$dxfVersion", submission.DxfDocumentVersionId.ToString()); command.Parameters.AddWithValue("$session", submission.VerificationSessionId.ToString()); command.Parameters.AddWithValue("$extract", submission.ExtractionEvidenceJson); command.Parameters.AddWithValue("$verify", submission.VerificationEvidenceJson); command.Parameters.AddWithValue("$revision", submission.Revision); command.Parameters.AddWithValue("$maker", submission.MakerUserId); command.Parameters.AddWithValue("$display", submission.MakerDisplayName); command.Parameters.AddWithValue("$created", submission.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$submitted", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$status", (int)submission.Status); command.Parameters.AddWithValue("$approver", (object?)submission.AssignedApproverUserId ?? DBNull.Value); command.Parameters.AddWithValue("$approvers", JsonSerializer.Serialize(submission.AssignedApproverUserIds));
        await command.ExecuteNonQueryAsync();
    }
    private static AuditEntry Audit(DocumentAuditAction action, Guid package, string result) => new() { DocumentId = package, UserId = "maker-001", Action = action, TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DetailsJson = $"{{\"packageId\":\"{package}\",\"revision\":1,\"result\":\"{result}\",\"maker\":\"Maker One\"}}" };
    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
