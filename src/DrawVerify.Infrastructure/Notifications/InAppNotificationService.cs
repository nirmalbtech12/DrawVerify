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
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", $"Review decision: {decision.DecisionType}. {decision.Comments}" ); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The Maker notification could not be persisted because the submission was not found.");
    }
    public async Task NotifyCheckerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO InAppNotification(NotificationId,UserId,PackageId,Revision,Message,CreatedUtc) SELECT $id,CheckerUserId,$package,$revision,$message,$created FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT $id,CheckerUserId,$package,$revision,$message,$created FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$package", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$message", $"Final approval decision: {decision.DecisionType}. {decision.Comments}"); command.Parameters.AddWithValue("$created", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(cancellationToken) < 1) throw new InvalidOperationException("The Checker notification could not be persisted because the review decision was not found.");
    }
}
