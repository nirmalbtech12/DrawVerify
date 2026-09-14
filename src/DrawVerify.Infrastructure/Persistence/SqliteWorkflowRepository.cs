using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Configuration;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for workflow state and history.
/// </summary>
public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;
    private readonly WorkflowConfiguration _configuration;
    private readonly ICentralizedLogger? _logger;
    public SqliteWorkflowRepository(string databasePath, ICentralizedLogger? logger = null, WorkflowConfiguration? configuration = null)
    {
        _configuration = configuration ?? new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, SubmittedForReviewState = WorkflowStatus.SubmittedForReview, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } };
        _configuration.Validate();
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _logger = logger;
        try { Initialize(); } catch (Exception ex) { logger?.Log(ex, "SQLite workflow migration", $"Unable to initialize workflow schema at '{databasePath}'."); throw; }
    }
    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS WorkflowState (PackageId TEXT PRIMARY KEY, Status INTEGER NOT NULL, Revision INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ReviewSubmission (SubmissionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL DEFAULT '', StepDocumentVersionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, VerificationSessionId TEXT NOT NULL, ExtractionEvidenceJson TEXT NOT NULL, VerificationEvidenceJson TEXT NOT NULL, Revision INTEGER NOT NULL, MakerUserId TEXT NOT NULL, MakerDisplayName TEXT NOT NULL, SubmittedUtc TEXT NOT NULL, Note TEXT NULL, Status INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS IX_ReviewSubmission_Package ON ReviewSubmission(PackageId); CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ReviewDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, CheckerUserId TEXT NOT NULL, CheckerDisplayName TEXT NOT NULL, DecidedUtc TEXT NOT NULL, DecisionType INTEGER NOT NULL, Comments TEXT NULL, SnapshotReference TEXT NOT NULL, SnapshotJson TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 0, UNIQUE(PackageId,Revision)); CREATE TABLE IF NOT EXISTS ReviewComment (CommentId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, AuthorUserId TEXT NOT NULL, AuthorDisplayName TEXT NOT NULL, CreatedUtc TEXT NOT NULL, Text TEXT NOT NULL); CREATE TABLE IF NOT EXISTS InAppNotification (NotificationId TEXT PRIMARY KEY, UserId TEXT NOT NULL, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, Message TEXT NOT NULL, CreatedUtc TEXT NOT NULL, IsRead INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS RevisionLock (PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, LockedUtc TEXT NOT NULL, PRIMARY KEY(PackageId,Revision));";
        cmd.ExecuteNonQuery();
        using (var finalTable = c.CreateCommand())
        {
            finalTable.CommandText = "CREATE TABLE IF NOT EXISTS FinalApprovalDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, CheckerUserId TEXT NOT NULL, CheckerDisplayName TEXT NOT NULL, DecidedUtc TEXT NOT NULL, DecisionType INTEGER NOT NULL, Comments TEXT NULL, SnapshotReference TEXT NOT NULL, SnapshotJson TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 1, UNIQUE(PackageId,Revision));";
            finalTable.ExecuteNonQuery();
        }
        foreach (var column in new[] { "DxfDocumentId TEXT NOT NULL DEFAULT ''", "AssignedCheckerUserId TEXT NULL", "AvailableToCheckers INTEGER NOT NULL DEFAULT 1", "AssignedApproverUserId TEXT NULL", "AvailableToApprovers INTEGER NOT NULL DEFAULT 1" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewSubmission ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        foreach (var column in new[] { "ApproverUserId TEXT NULL", "ApproverDisplayName TEXT NULL", "IsFinalApproval INTEGER NOT NULL DEFAULT 0" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewDecision ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
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
    public async Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(string? reviewerUserId, CancellationToken token = default)
    {
        var submissions = await GetSubmissionsAsync(Guid.Empty, token).ConfigureAwait(false); return submissions.Where(x => x.Status is WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview).Where(x => x.AvailableToCheckers || string.Equals(x.AssignedCheckerUserId, reviewerUserId, StringComparison.OrdinalIgnoreCase)).Select(x => new WorkflowQueueItem(x.PackageId, x.Revision, x.MakerDisplayName, x.SubmittedUtc, x.Status, x.VerificationEvidenceJson) { AssignedCheckerUserId = x.AssignedCheckerUserId, AvailableToCheckers = x.AvailableToCheckers }).ToArray();
    }
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken token = default) => GetCheckerQueueAsync(null, token);
    public async Task<IReadOnlyList<WorkflowQueueItem>> GetApproverQueueAsync(string? approverUserId, CancellationToken token = default)
    {
        var submissions = await GetSubmissionsAsync(Guid.Empty, token).ConfigureAwait(false);
        return submissions.Where(x => x.Status is WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval)
            .Where(x => x.AvailableToApprovers || string.Equals(x.AssignedApproverUserId, approverUserId, StringComparison.OrdinalIgnoreCase))
            .Select(x => new WorkflowQueueItem(x.PackageId, x.Revision, x.MakerDisplayName, x.SubmittedUtc, x.Status, x.VerificationEvidenceJson)
            { AssignedApproverUserId = x.AssignedApproverUserId, AvailableToApprovers = x.AvailableToApprovers }).ToArray();
    }
    public async Task LockRevisionAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT OR IGNORE INTO RevisionLock(PackageId,Revision,LockedUtc) VALUES($package,$revision,$locked)";
        cmd.Parameters.AddWithValue("$package", packageId.ToString()); cmd.Parameters.AddWithValue("$revision", revision); cmd.Parameters.AddWithValue("$locked", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)); await cmd.ExecuteNonQueryAsync(token);
    }
    public async Task<IReadOnlyList<ReviewDecision>> GetDecisionHistoryAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        var decision = await GetDecisionAsync(packageId, revision, token).ConfigureAwait(false);
        return decision is null ? Array.Empty<ReviewDecision>() : new[] { decision };
    }

    public async Task<ReviewPackage?> GetReviewPackageAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        var submissions = await GetSubmissionsAsync(packageId, token).ConfigureAwait(false);
        var submission = submissions.FirstOrDefault(x => x.Revision == revision);
        if (submission is null) return null;
        var decision = await GetDecisionAsync(packageId, revision, token).ConfigureAwait(false);
        var comments = await GetReviewCommentsAsync(packageId, revision, token).ConfigureAwait(false);
        return ReviewPackage.FromSubmission(submission, decision, comments);
    }

    public async Task RecordReviewStartedAsync(ReviewPackage package, AuditEntry auditEntry, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try { await Exec(c,t,"UPDATE WorkflowState SET Status=$underReview,UpdatedUtc=$updated WHERE PackageId=$package AND Revision=$revision AND Status=$submitted",token,("$underReview",(int)WorkflowStatus.UnderReview),("$updated",auditEntry.TimestampUtc.ToString("O",CultureInfo.InvariantCulture)),("$package",package.PackageId.ToString()),("$revision",package.Revision),("$submitted",(int)WorkflowStatus.SubmittedForReview)); await Exec(c,t,"UPDATE ReviewSubmission SET Status=$underReview WHERE PackageId=$package AND Revision=$revision AND Status=$submitted",token,("$underReview",(int)WorkflowStatus.UnderReview),("$package",package.PackageId.ToString()),("$revision",package.Revision),("$submitted",(int)WorkflowStatus.SubmittedForReview)); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; }
    }

    public async Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT CommentId,PackageId,Revision,AuthorUserId,AuthorDisplayName,CreatedUtc,Text FROM ReviewComment WHERE PackageId=$package AND Revision=$revision ORDER BY CreatedUtc"; cmd.Parameters.AddWithValue("$package", packageId.ToString()); cmd.Parameters.AddWithValue("$revision", revision); await using var r = await cmd.ExecuteReaderAsync(token); var result = new List<ReviewComment>(); while (await r.ReadAsync(token)) result.Add(new ReviewComment { CommentId=Guid.Parse(r.GetString(0)), PackageId=Guid.Parse(r.GetString(1)), Revision=r.GetInt32(2), AuthorUserId=r.GetString(3), AuthorDisplayName=r.GetString(4), CreatedUtc=DateTime.Parse(r.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Text=r.GetString(6) }); return result;
    }

    public async Task SaveReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(comment.Text)) throw new ArgumentException("Review comment cannot be blank.", nameof(comment));
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token); try { await Exec(c,t,"INSERT INTO ReviewComment VALUES($id,$package,$revision,$user,$display,$created,$text)",token,("$id",comment.CommentId.ToString()),("$package",comment.PackageId.ToString()),("$revision",comment.Revision),("$user",comment.AuthorUserId),("$display",comment.AuthorDisplayName),("$created",comment.CreatedUtc.ToString("O",CultureInfo.InvariantCulture)),("$text",comment.Text)); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; }
    }

    public async Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, CancellationToken token = default)
    {
        decision.Validate();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            await using var state = c.CreateCommand(); state.Transaction=t; state.CommandText="UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=$package AND Status IN ($submitted,$underReview,$approvedByChecker,$pendingFinal) AND Revision=$revision"; state.Parameters.AddWithValue("$status",(int)resultingStatus); state.Parameters.AddWithValue("$updated",decision.DecidedUtc.ToString("O",CultureInfo.InvariantCulture)); state.Parameters.AddWithValue("$package",decision.PackageId.ToString()); state.Parameters.AddWithValue("$submitted",(int)WorkflowStatus.SubmittedForReview); state.Parameters.AddWithValue("$underReview",(int)WorkflowStatus.UnderReview); state.Parameters.AddWithValue("$approvedByChecker",(int)WorkflowStatus.ApprovedByChecker); state.Parameters.AddWithValue("$pendingFinal",(int)WorkflowStatus.PendingFinalApproval); state.Parameters.AddWithValue("$revision",decision.Revision); if (await state.ExecuteNonQueryAsync(token) != 1) throw await CreateDecisionConflictAsync(c, t, decision.PackageId, decision.Revision, token);
            await Exec(c,t,"UPDATE ReviewSubmission SET Status=$status WHERE PackageId=$package AND Revision=$revision",token,("$status",(int)resultingStatus),("$package",decision.PackageId.ToString()),("$revision",decision.Revision));
            try
            {
                var table = decision.IsFinalApproval ? "FinalApprovalDecision" : "ReviewDecision";
                await Exec(c,t,$"INSERT INTO {table}(DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval) VALUES($id,$package,$revision,$user,$display,$time,$type,$comments,$reference,$snapshot,$approver,$approverDisplay,$final)",token,("$id",decision.DecisionId.ToString()),("$package",decision.PackageId.ToString()),("$revision",decision.Revision),("$user",decision.CheckerUserId),("$display",decision.CheckerDisplayName),("$time",decision.DecidedUtc.ToString("O",CultureInfo.InvariantCulture)),("$type",(int)decision.DecisionType),("$comments",(object?)decision.Comments??DBNull.Value),("$reference",decision.SnapshotReference),("$snapshot",decision.SnapshotJson),("$approver",(object?)decision.ApproverUserId??DBNull.Value),("$approverDisplay",(object?)decision.ApproverDisplayName??DBNull.Value),("$final",decision.IsFinalApproval ? 1 : 0));
            }
            catch (SqliteException exception) when (IsReviewDecisionConflict(exception))
            {
                throw await CreateDecisionConflictAsync(c, t, decision.PackageId, decision.Revision, token);
            }
            await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); return decision;
        }
        catch (ReviewDecisionConflictException) { await t.RollbackAsync(token); throw; }
        catch (Exception exception) { await t.RollbackAsync(token); _logger?.Log(exception, "SQLite review decision persistence", $"Unable to persist decision for packageId={decision.PackageId}, revision={decision.Revision}."); throw; }
    }

    private static bool IsReviewDecisionConflict(SqliteException exception)
        => exception.SqliteErrorCode == 19 && exception.Message.Contains("ReviewDecision.PackageId", StringComparison.OrdinalIgnoreCase) && exception.Message.Contains("ReviewDecision.Revision", StringComparison.OrdinalIgnoreCase);

    private async Task<ReviewDecisionConflictException> CreateDecisionConflictAsync(SqliteConnection? transactionConnection, SqliteTransaction? transaction, Guid packageId, int requestedRevision, CancellationToken token)
    {
        if (transactionConnection is not null)
            return await ReadDecisionConflictAsync(transactionConnection, transaction, packageId, requestedRevision, token);
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        return await ReadDecisionConflictAsync(c, null, packageId, requestedRevision, token);
    }

    private static async Task<ReviewDecisionConflictException> ReadDecisionConflictAsync(SqliteConnection c, SqliteTransaction? transaction, Guid packageId, int requestedRevision, CancellationToken token)
    {
        WorkflowStatus status = WorkflowStatus.Draft; var revision = 0;
        await using (var state = c.CreateCommand())
        {
            state.Transaction = transaction; state.CommandText = "SELECT Status,Revision FROM WorkflowState WHERE PackageId=$package"; state.Parameters.AddWithValue("$package", packageId.ToString());
            await using var reader = await state.ExecuteReaderAsync(token); if (await reader.ReadAsync(token)) { status = (WorkflowStatus)reader.GetInt32(0); revision = reader.GetInt32(1); }
        }
        ReviewDecision? prior = null;
        await using (var decision = c.CreateCommand())
        {
            decision.Transaction = transaction; decision.CommandText = "SELECT DecisionId,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT DecisionId,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision LIMIT 1"; decision.Parameters.AddWithValue("$package", packageId.ToString()); decision.Parameters.AddWithValue("$revision", requestedRevision);
            await using var reader = await decision.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) prior = new ReviewDecision { DecisionId=Guid.Parse(reader.GetString(0)), PackageId=packageId, Revision=requestedRevision, CheckerUserId=reader.GetString(1), CheckerDisplayName=reader.GetString(2), DecidedUtc=DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DecisionType=(ReviewDecisionType)reader.GetInt32(4), Comments=reader.IsDBNull(5) ? null : reader.GetString(5), SnapshotReference=reader.GetString(6), SnapshotJson=reader.GetString(7), ApproverUserId=reader.IsDBNull(8) ? string.Empty : reader.GetString(8), ApproverDisplayName=reader.IsDBNull(9) ? string.Empty : reader.GetString(9), IsFinalApproval=!reader.IsDBNull(10) && reader.GetInt32(10) != 0 };
        }
        return new ReviewDecisionConflictException(packageId, requestedRevision, status, revision, prior);
    }

    private async Task<ReviewDecision?> GetDecisionAsync(Guid packageId, int revision, CancellationToken token)
    {
        await using var c=new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT DecisionId,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT DecisionId,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision LIMIT 1"; cmd.Parameters.AddWithValue("$package",packageId.ToString()); cmd.Parameters.AddWithValue("$revision",revision); await using var r=await cmd.ExecuteReaderAsync(token); if(!await r.ReadAsync(token)) return null; return new ReviewDecision { DecisionId=Guid.Parse(r.GetString(0)),PackageId=packageId,Revision=revision,CheckerUserId=r.GetString(1),CheckerDisplayName=r.GetString(2),DecidedUtc=DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),DecisionType=(ReviewDecisionType)r.GetInt32(4),Comments=r.IsDBNull(5)?null:r.GetString(5),SnapshotReference=r.GetString(6),SnapshotJson=r.GetString(7),ApproverUserId=r.IsDBNull(8)?string.Empty:r.GetString(8),ApproverDisplayName=r.IsDBNull(9)?string.Empty:r.GetString(9),IsFinalApproval=!r.IsDBNull(10)&&r.GetInt32(10)!=0 };
    }
    public async Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expected, ReviewSubmission submission, AuditEntry audit, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            if (!submission.MakerAuthorized || string.IsNullOrWhiteSpace(submission.MakerUserId) || !string.Equals(submission.MakerUserId, audit.UserId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("A Maker authorization snapshot is required for workflow submission.");
            if (!_configuration.PermittedInitialStates.Contains(expected.Status)) throw new InvalidOperationException($"The package is in '{expected.Status}', and cannot be submitted for review.");
            await using var stateCommand = c.CreateCommand(); stateCommand.Transaction = t; stateCommand.CommandText = "SELECT Status,Revision FROM WorkflowState WHERE PackageId=$package"; stateCommand.Parameters.AddWithValue("$package", expected.PackageId.ToString());
            await using var stateReader = await stateCommand.ExecuteReaderAsync(token);
            var stateExists = await stateReader.ReadAsync(token);
            var currentStatus = stateExists ? stateReader.GetInt32(0) : (int)expected.Status;
            var currentRevision = stateExists ? stateReader.GetInt32(1) : expected.Revision;
            await stateReader.DisposeAsync();
            if (stateExists && (currentStatus != (int)expected.Status || currentRevision != expected.Revision)) throw new InvalidOperationException("The package workflow state changed before submission; refresh and try again.");
            if (stateExists)
                await Exec(c, t, "UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=$package AND Status=$expectedStatus AND Revision=$revision", token, ("$package", expected.PackageId.ToString()), ("$status", (int)_configuration.SubmittedForReviewState), ("$updated", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$expectedStatus", (int)expected.Status), ("$revision", expected.Revision));
            else
                await Exec(c, t, "INSERT INTO WorkflowState(PackageId,Status,Revision,UpdatedUtc) VALUES($package,$status,$revision,$updated)", token, ("$package", expected.PackageId.ToString()), ("$status", (int)_configuration.SubmittedForReviewState), ("$revision", expected.Revision), ("$updated", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)));
            await Exec(c, t, "INSERT INTO ReviewSubmission(SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,SubmittedUtc,Note,Status) VALUES($id,$package,$dxfDocument,$step,$dxf,$session,$extract,$verify,$revision,$maker,$display,$submitted,$note,$status)", token, ("$id", submission.SubmissionId.ToString()), ("$package", submission.PackageId.ToString()), ("$dxfDocument", submission.DxfDocumentId.ToString()), ("$step", submission.StepDocumentVersionId.ToString()), ("$dxf", submission.DxfDocumentVersionId.ToString()), ("$session", submission.VerificationSessionId.ToString()), ("$extract", submission.ExtractionEvidenceJson), ("$verify", submission.VerificationEvidenceJson), ("$revision", submission.Revision), ("$maker", submission.MakerUserId), ("$display", submission.MakerDisplayName), ("$submitted", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$note", (object?)submission.Note ?? DBNull.Value), ("$status", (int)submission.Status));
            await InsertAudit(c, t, audit, token); await t.CommitAsync(token); return submission;
        }
        catch { await t.RollbackAsync(token); throw; }
    }
    public async Task RecordAuditAsync(AuditEntry entry, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await InsertAudit(c, null, entry, token); }
    public async Task MarkSupersededAsync(Guid submissionId, AuditEntry audit, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token); try { await Exec(c,t,"UPDATE ReviewSubmission SET Status=$status WHERE SubmissionId=$id",token,("$status",(int)WorkflowStatus.Superseded),("$id",submissionId.ToString())); await Exec(c,t,"UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=(SELECT PackageId FROM ReviewSubmission WHERE SubmissionId=$id)",token,("$status",(int)WorkflowStatus.Superseded),("$updated",audit.TimestampUtc.ToString("O",CultureInfo.InvariantCulture)),("$id",submissionId.ToString())); await InsertAudit(c,t,audit,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; } }
    private const string SelectSubmission = "SELECT SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,SubmittedUtc,Note,Status,AssignedCheckerUserId,AvailableToCheckers,AssignedApproverUserId,AvailableToApprovers FROM ReviewSubmission";
    private static WorkflowState MapState(SqliteDataReader r) => new() { PackageId = Guid.Parse(r.GetString(0)), Status = (WorkflowStatus)r.GetInt32(1), Revision = r.GetInt32(2), UpdatedUtc = DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
    private static ReviewSubmission MapSubmission(SqliteDataReader r) => new() { SubmissionId=Guid.Parse(r.GetString(0)),PackageId=Guid.Parse(r.GetString(1)),DxfDocumentId=Guid.TryParse(r.GetString(2), out var dxfDocumentId) ? dxfDocumentId : Guid.Empty,StepDocumentVersionId=Guid.Parse(r.GetString(3)),DxfDocumentVersionId=Guid.Parse(r.GetString(4)),VerificationSessionId=Guid.Parse(r.GetString(5)),ExtractionEvidenceJson=r.GetString(6),VerificationEvidenceJson=r.GetString(7),Revision=r.GetInt32(8),MakerUserId=r.GetString(9),MakerDisplayName=r.GetString(10),SubmittedUtc=DateTime.Parse(r.GetString(11),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),Note=r.IsDBNull(12)?null:r.GetString(12),Status=(WorkflowStatus)r.GetInt32(13),AssignedCheckerUserId=r.IsDBNull(14)?null:r.GetString(14),AvailableToCheckers=r.GetInt32(15) != 0,AssignedApproverUserId=r.IsDBNull(16)?null:r.GetString(16),AvailableToApprovers=!r.IsDBNull(17) && r.GetInt32(17) != 0 };
    private static async Task InsertAudit(SqliteConnection c, SqliteTransaction? t, AuditEntry e, CancellationToken token) => await Exec(c,t,"INSERT INTO DocumentAuditLog VALUES($id,$document,$action,$user,$time,$details)",token,("$id",e.AuditId.ToString()),("$document",(object?)e.DocumentId?.ToString()??DBNull.Value),("$action",(int)e.Action),("$user",(object?)e.UserId??DBNull.Value),("$time",e.TimestampUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)),("$details",e.DetailsJson));
    private static async Task<int> Exec(SqliteConnection c, SqliteTransaction? t, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; foreach(var value in values) cmd.Parameters.AddWithValue(value.Name,value.Value); return await cmd.ExecuteNonQueryAsync(token); }
}
