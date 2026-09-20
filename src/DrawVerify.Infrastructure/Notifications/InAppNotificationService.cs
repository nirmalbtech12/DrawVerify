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
    public InAppNotificationService(string databasePath) => _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
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
    public async Task<IReadOnlyList<InAppNotification>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A notification user is required.", nameof(userId));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NotificationId,SubmissionId,UserId,PackageId,Revision,Message,CreatedUtc,IsRead,Target,EventType,Channel FROM InAppNotification WHERE UserId=$user ORDER BY CreatedUtc DESC"; command.Parameters.AddWithValue("$user", userId);
        var result = new List<InAppNotification>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new InAppNotification { NotificationId=Guid.Parse(reader.GetString(0)), SubmissionId=reader.IsDBNull(1) ? Guid.Empty : Guid.Parse(reader.GetString(1)), UserId=reader.GetString(2), PackageId=Guid.Parse(reader.GetString(3)), Revision=reader.GetInt32(4), Message=reader.GetString(5), CreatedUtc=DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), IsRead=reader.GetInt32(7) != 0, Target=reader.IsDBNull(8) ? string.Empty : reader.GetString(8), EventType=reader.IsDBNull(9) ? string.Empty : reader.GetString(9), Channel=reader.IsDBNull(10) ? string.Empty : reader.GetString(10) }); return result;
    }
    public async Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE InAppNotification SET IsRead=1 WHERE NotificationId=$id AND UserId=$user"; command.Parameters.AddWithValue("$id", notificationId.ToString()); command.Parameters.AddWithValue("$user", userId); if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException($"Notification {notificationId} was not found for the authenticated user.");
    }
    public async Task NotifyMakerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO InAppNotification(NotificationId,UserId,PackageId,Revision,Message,CreatedUtc) SELECT $id,MakerUserId,$package,$revision,$message,$created FROM ReviewSubmission WHERE PackageId=$package AND Revision=$revision";
        var status = decision.DecisionType switch { DrawVerify.Core.Enums.ReviewDecisionType.Reject => "Rejected", DrawVerify.Core.Enums.ReviewDecisionType.Return => "ReturnedForRework", DrawVerify.Core.Enums.ReviewDecisionType.RequestChanges => "ChangesRequested", DrawVerify.Core.Enums.ReviewDecisionType.Approve when decision.IsFinalApproval => "FinallyApproved", _ => "Approved" };
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", $"Document {packageId}, revision {revision}: status {status}. Reason: {decision.OverallReason ?? decision.Comments}. Correction: {decision.CorrectionAction ?? "See review comments"}. Comments: /workflow/{packageId:D}/{revision}/comments" ); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The Maker notification could not be persisted because the submission was not found.");
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
    private static async Task InsertNotificationAuditAsync(SqliteConnection connection, ReviewSubmission submission, string recipient, CancellationToken cancellationToken)
    {
        await using var audit = connection.CreateCommand(); audit.CommandText = "INSERT INTO DocumentAuditLog(AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,DocumentRevision) VALUES($id,$document,$action,$user,$time,$details,$revision)"; audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$document", submission.PackageId.ToString()); audit.Parameters.AddWithValue("$action", (int)DocumentAuditAction.CheckerNotificationSent); audit.Parameters.AddWithValue("$user", recipient); audit.Parameters.AddWithValue("$time", submission.SubmittedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); audit.Parameters.AddWithValue("$details", System.Text.Json.JsonSerializer.Serialize(new { submissionId = submission.SubmissionId, packageId = submission.PackageId, revision = submission.Revision, channel = WorkflowNotificationChannel.InApp.ToString(), recipient, eventType = "CheckerNotificationSent" })); audit.Parameters.AddWithValue("$revision", submission.Revision.ToString(CultureInfo.InvariantCulture)); await audit.ExecuteNonQueryAsync(cancellationToken);
    }
}
