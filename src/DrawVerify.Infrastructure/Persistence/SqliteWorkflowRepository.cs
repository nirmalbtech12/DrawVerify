using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for workflow state and history.
/// </summary>
public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;
    public SqliteWorkflowRepository(string databasePath, ICentralizedLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        try { Initialize(); } catch (Exception ex) { logger?.Log(ex, "SQLite workflow migration", $"Unable to initialize workflow schema at '{databasePath}'."); throw; }
    }
    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS WorkflowState (PackageId TEXT PRIMARY KEY, Status INTEGER NOT NULL, Revision INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ReviewSubmission (SubmissionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, StepDocumentVersionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, VerificationSessionId TEXT NOT NULL, ExtractionEvidenceJson TEXT NOT NULL, VerificationEvidenceJson TEXT NOT NULL, Revision INTEGER NOT NULL, MakerUserId TEXT NOT NULL, MakerDisplayName TEXT NOT NULL, SubmittedUtc TEXT NOT NULL, Note TEXT NULL, Status INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS IX_ReviewSubmission_Package ON ReviewSubmission(PackageId); CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }
    public async Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT PackageId,Status,Revision,UpdatedUtc FROM WorkflowState WHERE PackageId=$id"; cmd.Parameters.AddWithValue("$id", packageId.ToString()); await using var r = await cmd.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? MapState(r) : null;
    }
    public async Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand(); cmd.CommandText = SelectSubmission + " WHERE SubmissionId=$id"; cmd.Parameters.AddWithValue("$id", submissionId.ToString()); await using var r = await cmd.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? MapSubmission(r) : null;
    }
    public async Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand(); cmd.CommandText = SelectSubmission + (packageId == Guid.Empty ? " ORDER BY SubmittedUtc" : " WHERE PackageId=$id ORDER BY SubmittedUtc"); if (packageId != Guid.Empty) cmd.Parameters.AddWithValue("$id", packageId.ToString()); await using var r = await cmd.ExecuteReaderAsync(token); var result = new List<ReviewSubmission>(); while (await r.ReadAsync(token)) result.Add(MapSubmission(r)); return result;
    }
    public async Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken token = default)
    {
        var submissions = await GetSubmissionsAsync(Guid.Empty, token).ConfigureAwait(false); return submissions.Where(x => x.Status == WorkflowStatus.SubmittedForReview).Select(x => new WorkflowQueueItem(x.PackageId, x.Revision, x.MakerDisplayName, x.SubmittedUtc, x.Status, x.VerificationEvidenceJson)).ToArray();
    }
    public async Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expected, ReviewSubmission submission, AuditEntry audit, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            if (!submission.MakerAuthorized || string.IsNullOrWhiteSpace(submission.MakerUserId) || !string.Equals(submission.MakerUserId, audit.UserId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("A Maker authorization snapshot is required for workflow submission.");
            if (expected.Status is not (WorkflowStatus.Draft or WorkflowStatus.InPreparation)) throw new InvalidOperationException($"The package is in '{expected.Status}', and cannot be submitted for review.");
            await using var stateCommand = c.CreateCommand(); stateCommand.Transaction = t; stateCommand.CommandText = "SELECT Status,Revision FROM WorkflowState WHERE PackageId=$package"; stateCommand.Parameters.AddWithValue("$package", expected.PackageId.ToString());
            await using var stateReader = await stateCommand.ExecuteReaderAsync(token);
            var stateExists = await stateReader.ReadAsync(token);
            var currentStatus = stateExists ? stateReader.GetInt32(0) : (int)expected.Status;
            var currentRevision = stateExists ? stateReader.GetInt32(1) : expected.Revision;
            await stateReader.DisposeAsync();
            if (stateExists && (currentStatus != (int)expected.Status || currentRevision != expected.Revision)) throw new InvalidOperationException("The package workflow state changed before submission; refresh and try again.");
            if (stateExists)
                await Exec(c, t, "UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=$package AND Status=$expectedStatus AND Revision=$revision", token, ("$package", expected.PackageId.ToString()), ("$status", (int)WorkflowStatus.SubmittedForReview), ("$updated", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$expectedStatus", (int)expected.Status), ("$revision", expected.Revision));
            else
                await Exec(c, t, "INSERT INTO WorkflowState(PackageId,Status,Revision,UpdatedUtc) VALUES($package,$status,$revision,$updated)", token, ("$package", expected.PackageId.ToString()), ("$status", (int)WorkflowStatus.SubmittedForReview), ("$revision", expected.Revision), ("$updated", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)));
            await Exec(c, t, "INSERT INTO ReviewSubmission VALUES($id,$package,$step,$dxf,$session,$extract,$verify,$revision,$maker,$display,$submitted,$note,$status)", token, ("$id", submission.SubmissionId.ToString()), ("$package", submission.PackageId.ToString()), ("$step", submission.StepDocumentVersionId.ToString()), ("$dxf", submission.DxfDocumentVersionId.ToString()), ("$session", submission.VerificationSessionId.ToString()), ("$extract", submission.ExtractionEvidenceJson), ("$verify", submission.VerificationEvidenceJson), ("$revision", submission.Revision), ("$maker", submission.MakerUserId), ("$display", submission.MakerDisplayName), ("$submitted", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$note", (object?)submission.Note ?? DBNull.Value), ("$status", (int)submission.Status));
            await InsertAudit(c, t, audit, token); await t.CommitAsync(token); return submission;
        }
        catch { await t.RollbackAsync(token); throw; }
    }
    public async Task RecordAuditAsync(AuditEntry entry, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await InsertAudit(c, null, entry, token); }
    public async Task MarkSupersededAsync(Guid submissionId, AuditEntry audit, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token); try { await Exec(c,t,"UPDATE ReviewSubmission SET Status=$status WHERE SubmissionId=$id",token,("$status",(int)WorkflowStatus.Superseded),("$id",submissionId.ToString())); await Exec(c,t,"UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=(SELECT PackageId FROM ReviewSubmission WHERE SubmissionId=$id)",token,("$status",(int)WorkflowStatus.Superseded),("$updated",audit.TimestampUtc.ToString("O",CultureInfo.InvariantCulture)),("$id",submissionId.ToString())); await InsertAudit(c,t,audit,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; } }
    private const string SelectSubmission = "SELECT SubmissionId,PackageId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,SubmittedUtc,Note,Status FROM ReviewSubmission";
    private static WorkflowState MapState(SqliteDataReader r) => new() { PackageId = Guid.Parse(r.GetString(0)), Status = (WorkflowStatus)r.GetInt32(1), Revision = r.GetInt32(2), UpdatedUtc = DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
    private static ReviewSubmission MapSubmission(SqliteDataReader r) => new() { SubmissionId=Guid.Parse(r.GetString(0)),PackageId=Guid.Parse(r.GetString(1)),StepDocumentVersionId=Guid.Parse(r.GetString(2)),DxfDocumentVersionId=Guid.Parse(r.GetString(3)),VerificationSessionId=Guid.Parse(r.GetString(4)),ExtractionEvidenceJson=r.GetString(5),VerificationEvidenceJson=r.GetString(6),Revision=r.GetInt32(7),MakerUserId=r.GetString(8),MakerDisplayName=r.GetString(9),SubmittedUtc=DateTime.Parse(r.GetString(10),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),Note=r.IsDBNull(11)?null:r.GetString(11),Status=(WorkflowStatus)r.GetInt32(12) };
    private static async Task InsertAudit(SqliteConnection c, SqliteTransaction? t, AuditEntry e, CancellationToken token) => await Exec(c,t,"INSERT INTO DocumentAuditLog VALUES($id,$document,$action,$user,$time,$details)",token,("$id",e.AuditId.ToString()),("$document",(object?)e.DocumentId?.ToString()??DBNull.Value),("$action",(int)e.Action),("$user",(object?)e.UserId??DBNull.Value),("$time",e.TimestampUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)),("$details",e.DetailsJson));
    private static async Task<int> Exec(SqliteConnection c, SqliteTransaction? t, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; foreach(var value in values) cmd.Parameters.AddWithValue(value.Name,value.Value); return await cmd.ExecuteNonQueryAsync(token); }
}
