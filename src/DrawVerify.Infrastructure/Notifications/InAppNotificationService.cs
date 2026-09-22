using System.Globalization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Notifications;

/// <summary>
/// In-application notification delivery implementing INotificationService.
/// </summary>
public sealed class InAppNotificationService : INotificationService
{
    private readonly string _connectionString;
    private readonly ICentralizedLogger? _logger;
    public InAppNotificationService(string databasePath, ICentralizedLogger? logger = null) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); _logger = logger; }
    public async Task NotifySubmissionAsync(ReviewSubmission submission, IReadOnlyList<WorkflowNotificationChannel> channels, CancellationToken cancellationToken = default)
    {
        var recipients = submission.AssignedCheckerUserIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (recipients.Length == 0 && !string.IsNullOrWhiteSpace(submission.AssignedCheckerUserId)) recipients = new[] { submission.AssignedCheckerUserId };
        if (recipients.Length == 0) throw new InvalidOperationException($"No Checker is assigned to submission {submission.SubmissionId} for package {submission.PackageId}.");
        var supportedChannels = channels.Distinct().ToArray();
        if (supportedChannels.Length == 0 || supportedChannels.Any(x => x != WorkflowNotificationChannel.InApp)) throw new NotSupportedException("Only the configured in-app notification channel is available.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            var notificationId = Guid.NewGuid();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO InAppNotification(NotificationId,SubmissionId,UserId,PackageId,Revision,Message,CreatedUtc,IsRead,Target,EventType,Channel) VALUES($id,$submission,$user,$package,$revision,$message,$created,0,$target,$event,$channel)";
            command.Parameters.AddWithValue("$id", notificationId.ToString()); command.Parameters.AddWithValue("$submission", submission.SubmissionId.ToString()); command.Parameters.AddWithValue("$user", recipient); command.Parameters.AddWithValue("$package", submission.PackageId.ToString()); command.Parameters.AddWithValue("$revision", submission.Revision); command.Parameters.AddWithValue("$message", BuildSubmissionMessage(submission)); command.Parameters.AddWithValue("$created", submission.SubmittedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$target", $"/workflow/{submission.PackageId:D}/{submission.Revision}"); command.Parameters.AddWithValue("$event", "CheckerNotificationSent"); command.Parameters.AddWithValue("$channel", WorkflowNotificationChannel.InApp.ToString());
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
            if (inserted == 1) await InsertNotificationAuditAsync(connection, submission, recipient, cancellationToken);
        }
    }
    public async Task NotifyAdministratorAsync(Guid templateId, int templateVersion, Guid packageId, int revision, Guid placeholderId, string placeholderLabel, string reason, CancellationToken cancellationToken = default)
    {
        if (templateId == Guid.Empty || templateVersion < 1 || packageId == Guid.Empty || revision < 1 || placeholderId == Guid.Empty) throw new InvalidDataException("Configuration warning notification requires valid template, document, revision, and placeholder identity.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO InAppNotification(NotificationId,UserId,PackageId,Revision,Message,CreatedUtc) VALUES($id,$user,$package,$revision,$message,$created)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$user", "Admin"); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", $"Template {templateId} version {templateVersion} has an invalid signature placeholder '{placeholderLabel}' ({placeholderId}) for document {packageId}, revision {revision}: {reason}"); command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task NotifyApproverAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        if (packageId == Guid.Empty || revision < 1 || decision.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(decision.CheckerUserId) || string.IsNullOrWhiteSpace(decision.CheckerDisplayName) || decision.DecidedUtc == default)
            throw new InvalidDataException("Approval notification requires package, revision, Checker identity, event identity, and timestamp.");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var submission = await ReadApprovalSubmissionAsync(connection, packageId, revision, cancellationToken).ConfigureAwait(false);
        var recipients = submission.ApproverIds;
        if (recipients.Length == 0) throw new InvalidOperationException($"No Approver is assigned to package {packageId}, revision {revision}.");
        var message = BuildApprovalMessage(packageId, revision, decision);
        foreach (var recipient in recipients)
            await PersistApproverNotificationWithRetryAsync(connection, submission.SubmissionId, packageId, revision, decision, recipient, message, cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<InAppNotification>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A notification user is required.", nameof(userId));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NotificationId,SubmissionId,UserId,PackageId,Revision,Message,CreatedUtc,IsRead,Target,EventType,Channel,ApprovalEventId,RejectionEventId FROM InAppNotification WHERE UserId=$user ORDER BY CreatedUtc DESC"; command.Parameters.AddWithValue("$user", userId);
        var result = new List<InAppNotification>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new InAppNotification { NotificationId=Guid.Parse(reader.GetString(0)), SubmissionId=reader.IsDBNull(1) ? Guid.Empty : Guid.Parse(reader.GetString(1)), UserId=reader.GetString(2), PackageId=Guid.Parse(reader.GetString(3)), Revision=reader.GetInt32(4), Message=reader.GetString(5), CreatedUtc=DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), IsRead=reader.GetInt32(7) != 0, Target=reader.IsDBNull(8) ? string.Empty : reader.GetString(8), EventType=reader.IsDBNull(9) ? string.Empty : reader.GetString(9), Channel=reader.IsDBNull(10) ? string.Empty : reader.GetString(10), ApprovalEventId=reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)), RejectionEventId=reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12)) }); return result;
    }
    public async Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE InAppNotification SET IsRead=1 WHERE NotificationId=$id AND UserId=$user"; command.Parameters.AddWithValue("$id", notificationId.ToString()); command.Parameters.AddWithValue("$user", userId); if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException($"Notification {notificationId} was not found for the authenticated user.");
    }
    public async Task NotifyMakerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision.DecisionType is not (ReviewDecisionType.Reject or ReviewDecisionType.Return))
        {
            await using var legacyConnection = new SqliteConnection(_connectionString); await legacyConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var legacyCommand = legacyConnection.CreateCommand();
            legacyCommand.CommandText = "INSERT INTO InAppNotification(NotificationId,UserId,PackageId,Revision,Message,CreatedUtc) SELECT $id,MakerUserId,$package,$revision,$message,$created FROM ReviewSubmission WHERE PackageId=$package AND Revision=$revision";
            var legacyStatus = decision.DecisionType switch { ReviewDecisionType.Approve when decision.IsFinalApproval => "FinallyApproved", _ => "Approved" };
            legacyCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); legacyCommand.Parameters.AddWithValue("$package", packageId.ToString()); legacyCommand.Parameters.AddWithValue("$revision", revision); legacyCommand.Parameters.AddWithValue("$message", $"Document {packageId}, revision {revision}: status {legacyStatus}. Reason: {decision.OverallReason ?? decision.Comments}. Correction: {decision.CorrectionAction ?? "See review comments"}. Comments: /workflow/{packageId:D}/{revision}/comments"); legacyCommand.Parameters.AddWithValue("$created", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture));
            if (await legacyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("The Maker notification could not be persisted because the submission was not found.");
            return;
        }
        if (packageId == Guid.Empty || revision < 1 || decision.DecisionId == Guid.Empty || decision.DecidedUtc == default)
            throw new InvalidDataException("Maker rejection notification requires package, revision, rejection event identity, rejection type, and timestamp.");
        if (string.IsNullOrWhiteSpace(decision.OverallReason ?? decision.Comments)) throw new InvalidDataException("Maker rejection notification requires a rejection reason or comment.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        string? makerUserId; Guid submissionId;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT SubmissionId,MakerUserId FROM ReviewSubmission WHERE PackageId=$package AND Revision=$revision ORDER BY SubmittedUtc DESC, rowid DESC LIMIT 1";
            lookup.Parameters.AddWithValue("$package", packageId.ToString()); lookup.Parameters.AddWithValue("$revision", revision);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new KeyNotFoundException($"The submitted package {packageId}, revision {revision} was not found for Maker notification.");
            submissionId = Guid.Parse(reader.GetString(0)); makerUserId = reader.GetString(1);
        }
        if (string.IsNullOrWhiteSpace(makerUserId)) throw new InvalidOperationException($"The Maker identity is missing for package {packageId}, revision {revision}.");
        var rejectorRole = string.IsNullOrWhiteSpace(decision.ApproverUserId) ? "Checker" : "Approver";
        var rejectorName = string.IsNullOrWhiteSpace(decision.ApproverDisplayName) ? decision.CheckerDisplayName : decision.ApproverDisplayName;
        if (string.IsNullOrWhiteSpace(rejectorName)) throw new InvalidDataException($"The {rejectorRole} identity is missing for rejection event {decision.DecisionId}.");
        var status = decision.DecisionType == ReviewDecisionType.Reject ? "Rejected" : "Returned for Rework";
        var message = $"Document {packageId}, revision {revision}: {status} - action required. {rejectorRole} {rejectorName} at {decision.DecidedUtc.ToUniversalTime():O}. Reason: {decision.OverallReason ?? decision.Comments}. Correction: {decision.CorrectionAction ?? "See review comments"}. Open: /workflow/{packageId:D}/{revision}";
        await PersistMakerNotificationWithRetryAsync(connection, submissionId, packageId, revision, decision, makerUserId, rejectorRole, rejectorName, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistMakerNotificationWithRetryAsync(SqliteConnection connection, Guid submissionId, Guid packageId, int revision, ReviewDecision decision, string recipient, string rejectorRole, string rejectorName, string message, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (var replay = connection.CreateCommand())
                {
                    replay.Transaction = transaction; replay.CommandText = "SELECT 1 FROM InAppNotification WHERE RejectionEventId=$event AND UserId=$user AND Channel=$channel LIMIT 1";
                    replay.Parameters.AddWithValue("$event", decision.DecisionId.ToString()); replay.Parameters.AddWithValue("$user", recipient); replay.Parameters.AddWithValue("$channel", WorkflowNotificationChannel.InApp.ToString());
                    if (await replay.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null) { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return; }
                }
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction; command.CommandText = "INSERT OR IGNORE INTO InAppNotification(NotificationId,SubmissionId,RejectionEventId,UserId,PackageId,Revision,Message,CreatedUtc,IsRead,Target,EventType,Channel) VALUES($id,$submission,$event,$user,$package,$revision,$message,$created,0,$target,$type,$channel)";
                    command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$submission", submissionId.ToString()); command.Parameters.AddWithValue("$event", decision.DecisionId.ToString()); command.Parameters.AddWithValue("$user", recipient); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", message); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$target", $"/workflow/{packageId:D}/{revision}"); command.Parameters.AddWithValue("$type", "MakerNotificationSent"); command.Parameters.AddWithValue("$channel", WorkflowNotificationChannel.InApp.ToString());
                    if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
                    {
                        await using var audit = connection.CreateCommand(); audit.Transaction = transaction; audit.CommandText = "INSERT INTO DocumentAuditLog(AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,DocumentRevision) VALUES($id,$document,$action,$user,$time,$details,$revision)";
                        var rejectorUserId = string.Equals(rejectorRole, "Approver", StringComparison.OrdinalIgnoreCase) ? decision.ApproverUserId : decision.CheckerUserId;
                        audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$document", packageId.ToString()); audit.Parameters.AddWithValue("$action", (int)DocumentAuditAction.MakerNotificationSent); audit.Parameters.AddWithValue("$user", recipient); audit.Parameters.AddWithValue("$time", decision.DecidedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); audit.Parameters.AddWithValue("$details", System.Text.Json.JsonSerializer.Serialize(new { packageId, revision, rejectionEventId = decision.DecisionId, channel = WorkflowNotificationChannel.InApp.ToString(), recipient, rejectorUserId, rejectorName, rejectorRole, eventType = "MakerNotificationSent", rejectionTimestampUtc = decision.DecidedUtc.ToUniversalTime(), reason = decision.OverallReason ?? decision.Comments, correctionAction = decision.CorrectionAction })); audit.Parameters.AddWithValue("$revision", revision.ToString(CultureInfo.InvariantCulture)); await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return;
            }
            catch (SqliteException exception) when (attempt < maxAttempts)
            {
                _logger?.Log(exception, "Maker notification retry", $"Retrying packageId={packageId}, revision={revision}, rejectionEventId={decision.DecisionId}, recipient={recipient}, channel={WorkflowNotificationChannel.InApp}, attempt={attempt}.");
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.Log(exception, "Maker notification delivery", $"Unable to deliver packageId={packageId}, revision={revision}, rejectionEventId={decision.DecisionId}, recipient={recipient}, channel={WorkflowNotificationChannel.InApp}.");
                throw;
            }
        }
    }
    public async Task NotifyCheckerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        string? checkerUserId;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT CheckerUserId FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision AND IsFinalApproval=0 ORDER BY DecidedUtc LIMIT 1";
            lookup.Parameters.AddWithValue("$package", packageId.ToString()); lookup.Parameters.AddWithValue("$revision", revision);
            checkerUserId = (await lookup.ExecuteScalarAsync(cancellationToken)) as string;
        }
        if (string.IsNullOrWhiteSpace(checkerUserId)) throw new InvalidOperationException("The original Checker identity could not be resolved for notification delivery.");
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO InAppNotification(NotificationId,UserId,PackageId,Revision,Message,CreatedUtc) VALUES($id,$user,$package,$revision,$message,$created)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$user", checkerUserId); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", $"Final approval decision: {decision.DecisionType}. {decision.Comments}"); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildSubmissionMessage(ReviewSubmission submission) => $"Document {submission.PackageId}, revision {submission.Revision}, submitted by {submission.MakerDisplayName} at {submission.SubmittedUtc.ToUniversalTime():O}: submitted for review.";
    private static string BuildApprovalMessage(Guid packageId, int revision, ReviewDecision decision) => $"Document {packageId}, revision {revision}: approved by Checker {decision.CheckerDisplayName} at {decision.DecidedUtc.ToUniversalTime():O} - ready for final approval.";
    private sealed record ApprovalSubmission(Guid SubmissionId, string[] ApproverIds);
    private static async Task<ApprovalSubmission> ReadApprovalSubmissionAsync(SqliteConnection connection, Guid packageId, int revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SubmissionId,AssignedApproverUserId,AssignedApproverUserIdsJson FROM ReviewSubmission WHERE PackageId=$package AND Revision=$revision ORDER BY SubmittedUtc DESC, rowid DESC LIMIT 1";
        command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new KeyNotFoundException($"The submitted package {packageId}, revision {revision} was not found.");
        var legacy = reader.IsDBNull(1) ? null : reader.GetString(1);
        var assigned = reader.IsDBNull(2) ? Array.Empty<string>() : System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? throw new InvalidDataException("Stored Approver assignment data is invalid.");
        if (assigned.Length == 0 && !string.IsNullOrWhiteSpace(legacy)) assigned = new[] { legacy };
        return new ApprovalSubmission(Guid.Parse(reader.GetString(0)), assigned.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    private async Task PersistApproverNotificationWithRetryAsync(SqliteConnection connection, Guid submissionId, Guid packageId, int revision, ReviewDecision decision, string recipient, string message, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (var replay = connection.CreateCommand())
                {
                    replay.Transaction = transaction;
                    replay.CommandText = "SELECT 1 FROM InAppNotification WHERE ApprovalEventId=$event AND UserId=$user AND Channel=$channel LIMIT 1";
                    replay.Parameters.AddWithValue("$event", decision.DecisionId.ToString()); replay.Parameters.AddWithValue("$user", recipient); replay.Parameters.AddWithValue("$channel", WorkflowNotificationChannel.InApp.ToString());
                    if (await replay.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "INSERT OR IGNORE INTO InAppNotification(NotificationId,SubmissionId,ApprovalEventId,UserId,PackageId,Revision,Message,CreatedUtc,IsRead,Target,EventType,Channel) VALUES($id,$submission,$event,$user,$package,$revision,$message,$created,0,$target,$type,$channel)";
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$submission", submissionId.ToString()); command.Parameters.AddWithValue("$event", decision.DecisionId.ToString()); command.Parameters.AddWithValue("$user", recipient); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", message); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$target", $"/workflow/{packageId:D}/{revision}"); command.Parameters.AddWithValue("$type", "ApproverNotificationSent"); command.Parameters.AddWithValue("$channel", WorkflowNotificationChannel.InApp.ToString());
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
                {
                    await using var audit = connection.CreateCommand(); audit.Transaction = transaction;
                    audit.CommandText = "INSERT INTO DocumentAuditLog(AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,DocumentRevision) VALUES($id,$document,$action,$user,$time,$details,$revision)";
                    audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$document", packageId.ToString()); audit.Parameters.AddWithValue("$action", (int)DocumentAuditAction.ApproverNotificationSent); audit.Parameters.AddWithValue("$user", recipient); audit.Parameters.AddWithValue("$time", decision.DecidedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); audit.Parameters.AddWithValue("$details", System.Text.Json.JsonSerializer.Serialize(new { packageId, revision, approvalEventId = decision.DecisionId, checkerUserId = decision.CheckerUserId, checkerDisplayName = decision.CheckerDisplayName, channel = WorkflowNotificationChannel.InApp.ToString(), recipient, eventType = "ApproverNotificationSent", approvalTimestampUtc = decision.DecidedUtc.ToUniversalTime() })); audit.Parameters.AddWithValue("$revision", revision.ToString(CultureInfo.InvariantCulture)); await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SqliteException exception) when (attempt < maxAttempts)
            {
                _logger?.Log(exception, "Approver notification retry", $"Retrying packageId={packageId}, revision={revision}, approvalEventId={decision.DecisionId}, recipient={recipient}, attempt={attempt}.");
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.Log(exception, "Approver notification delivery", $"Unable to deliver packageId={packageId}, revision={revision}, approvalEventId={decision.DecisionId}, recipient={recipient}.");
                throw;
            }
        }
    }
    private static async Task InsertNotificationAuditAsync(SqliteConnection connection, ReviewSubmission submission, string recipient, CancellationToken cancellationToken)
    {
        await using var audit = connection.CreateCommand(); audit.CommandText = "INSERT INTO DocumentAuditLog(AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,DocumentRevision) VALUES($id,$document,$action,$user,$time,$details,$revision)"; audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$document", submission.PackageId.ToString()); audit.Parameters.AddWithValue("$action", (int)DocumentAuditAction.CheckerNotificationSent); audit.Parameters.AddWithValue("$user", recipient); audit.Parameters.AddWithValue("$time", submission.SubmittedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); audit.Parameters.AddWithValue("$details", System.Text.Json.JsonSerializer.Serialize(new { submissionId = submission.SubmissionId, packageId = submission.PackageId, revision = submission.Revision, channel = WorkflowNotificationChannel.InApp.ToString(), recipient, eventType = "CheckerNotificationSent" })); audit.Parameters.AddWithValue("$revision", submission.Revision.ToString(CultureInfo.InvariantCulture)); await audit.ExecuteNonQueryAsync(cancellationToken);
    }
}
