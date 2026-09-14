using System.Globalization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Notifications;

/// <summary>
/// In-application notification delivery implementing INotificationService.
/// </summary>
public sealed class InAppNotificationService : INotificationService
{
    private readonly string _connectionString;
    public InAppNotificationService(string databasePath) => _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
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
}
