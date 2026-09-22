using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Configuration;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for workflow state and history.
/// </summary>
public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;
    private readonly WorkflowConfiguration _configuration;
    private readonly ICentralizedLogger? _logger;
    private readonly IWorkflowConfigurationRepository? _configurationRepository;
    public SqliteWorkflowRepository(string databasePath, ICentralizedLogger? logger = null, WorkflowConfiguration? configuration = null, IWorkflowConfigurationRepository? configurationRepository = null)
    {
        _configuration = configuration ?? new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, SubmittedForReviewState = WorkflowStatus.SubmittedForReview, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } };
        _configuration.Validate();
        _configurationRepository = configurationRepository;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _logger = logger;
        try { Initialize(); } catch (Exception ex) { logger?.Log(ex, "SQLite workflow migration", $"Unable to initialize workflow schema at '{databasePath}'."); throw; }
    }
    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS WorkflowState (PackageId TEXT PRIMARY KEY, Status INTEGER NOT NULL, Revision INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS WorkflowInstances (WorkflowId TEXT PRIMARY KEY, ConfigurationVersion INTEGER NULL, StartedUtc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ReviewSubmission (SubmissionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL DEFAULT '', StepDocumentVersionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, VerificationSessionId TEXT NOT NULL, ExtractionEvidenceJson TEXT NOT NULL, VerificationEvidenceJson TEXT NOT NULL, Revision INTEGER NOT NULL, MakerUserId TEXT NOT NULL, MakerDisplayName TEXT NOT NULL, CreatedUtc TEXT NULL, SubmittedUtc TEXT NOT NULL, Note TEXT NULL, Status INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS IX_ReviewSubmission_Package ON ReviewSubmission(PackageId); CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ReviewDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, CheckerUserId TEXT NOT NULL, CheckerDisplayName TEXT NOT NULL, DecidedUtc TEXT NOT NULL, DecisionType INTEGER NOT NULL, Comments TEXT NULL, SnapshotReference TEXT NOT NULL, SnapshotJson TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 0, OverallReason TEXT NULL, CorrectionAction TEXT NULL, PriorDecisionId TEXT NULL, Route INTEGER NOT NULL DEFAULT 0, DestinationRole TEXT NULL, DestinationQueue TEXT NULL); CREATE TABLE IF NOT EXISTS ReviewComment (CommentId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, AuthorUserId TEXT NOT NULL, AuthorDisplayName TEXT NOT NULL, CreatedUtc TEXT NOT NULL, Text TEXT NOT NULL); CREATE TABLE IF NOT EXISTS DocumentMarkup (MarkupId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, DocumentVersionId TEXT NOT NULL, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, AuthorUserId TEXT NOT NULL, AuthorDisplayName TEXT NOT NULL, CreatedUtc TEXT NOT NULL, MarkupType TEXT NOT NULL, DataJson TEXT NOT NULL, PageNumber INTEGER NOT NULL, Location TEXT NULL); CREATE INDEX IF NOT EXISTS IX_DocumentMarkup_Version ON DocumentMarkup(DocumentId,DocumentVersionId); CREATE TABLE IF NOT EXISTS InAppNotification (NotificationId TEXT PRIMARY KEY, SubmissionId TEXT NULL, UserId TEXT NOT NULL, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, Message TEXT NOT NULL, CreatedUtc TEXT NOT NULL, IsRead INTEGER NOT NULL DEFAULT 0, Target TEXT NULL, EventType TEXT NULL, Channel TEXT NULL, ApprovalEventId TEXT NULL, RejectionEventId TEXT NULL); CREATE TABLE IF NOT EXISTS RevisionLock (PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, LockedUtc TEXT NOT NULL, PRIMARY KEY(PackageId,Revision));";
        cmd.ExecuteNonQuery();
        SqliteAuditRepository.EnsureSchema(c);
        using (var finalTable = c.CreateCommand())
        {
            finalTable.CommandText = "CREATE TABLE IF NOT EXISTS FinalApprovalDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, CheckerUserId TEXT NOT NULL, CheckerDisplayName TEXT NOT NULL, DecidedUtc TEXT NOT NULL, DecisionType INTEGER NOT NULL, Comments TEXT NULL, SnapshotReference TEXT NOT NULL, SnapshotJson TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 1, OverallReason TEXT NULL, CorrectionAction TEXT NULL, PriorDecisionId TEXT NULL, Route INTEGER NOT NULL DEFAULT 0, DestinationRole TEXT NULL, DestinationQueue TEXT NULL, UNIQUE(PackageId,Revision));";
            finalTable.ExecuteNonQuery();
        }
        foreach (var column in new[] { "DxfDocumentId TEXT NOT NULL DEFAULT ''", "AssignedCheckerUserId TEXT NULL", "AvailableToCheckers INTEGER NOT NULL DEFAULT 1", "AssignedApproverUserId TEXT NULL", "AvailableToApprovers INTEGER NOT NULL DEFAULT 1", "AssignedApproverUserIdsJson TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewSubmission ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        try { using var alter = c.CreateCommand(); alter.CommandText = "ALTER TABLE ReviewSubmission ADD COLUMN CreatedUtc TEXT NULL;"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        try { using var alter = c.CreateCommand(); alter.CommandText = "ALTER TABLE ReviewSubmission ADD COLUMN AssignedCheckerUserIdsJson TEXT NULL;"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        foreach (var column in new[] { "SubmissionId TEXT NULL", "Target TEXT NULL", "EventType TEXT NULL", "Channel TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE InAppNotification ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        try { using var alter = c.CreateCommand(); alter.CommandText = "ALTER TABLE InAppNotification ADD COLUMN ApprovalEventId TEXT NULL;"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        try { using var alter = c.CreateCommand(); alter.CommandText = "ALTER TABLE InAppNotification ADD COLUMN RejectionEventId TEXT NULL;"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        using (var unique = c.CreateCommand()) { unique.CommandText = "DROP INDEX IF EXISTS UX_InAppNotification_Submission_User; UPDATE InAppNotification SET ApprovalEventId=COALESCE(ApprovalEventId,SubmissionId) WHERE EventType='ApproverNotificationSent' OR EventType IS NULL; CREATE UNIQUE INDEX IF NOT EXISTS UX_InAppNotification_ApprovalEvent_User_Channel ON InAppNotification(ApprovalEventId,UserId,Channel) WHERE EventType='ApproverNotificationSent'; CREATE UNIQUE INDEX IF NOT EXISTS UX_InAppNotification_RejectionEvent_User_Channel ON InAppNotification(RejectionEventId,UserId,Channel) WHERE EventType='MakerNotificationSent'; CREATE UNIQUE INDEX IF NOT EXISTS UX_InAppNotification_Submission_User_Channel ON InAppNotification(SubmissionId,UserId,Channel) WHERE EventType='CheckerNotificationSent';"; unique.ExecuteNonQuery(); }
        foreach (var column in new[] { "FingerprintJson TEXT NULL", "FingerprintAlgorithm TEXT NULL", "FingerprintCanonicalizationVersion INTEGER NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewSubmission ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        foreach (var column in new[] { "MakerSignatureJson TEXT NULL", "MakerSignatureFinalized INTEGER NULL", "CheckerSignatureJson TEXT NULL", "CheckerSignatureFinalized INTEGER NULL", "ApproverSignatureJson TEXT NULL", "ApproverSignatureFinalized INTEGER NULL", "InternalApprovalStampJson TEXT NULL", "InternalApprovalStampFinalized INTEGER NULL", "TemplateMappingJson TEXT NULL", "PlaceholderAssignmentsJson TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewSubmission ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        using (var stampGuards = c.CreateCommand())
        {
            stampGuards.CommandText = "DROP TRIGGER IF EXISTS TR_ReviewSubmission_ApprovalStampUpdate; DROP TRIGGER IF EXISTS TR_ReviewSubmission_ApprovalStampDelete; CREATE TRIGGER TR_ReviewSubmission_ApprovalStampUpdate BEFORE UPDATE OF InternalApprovalStampJson,InternalApprovalStampFinalized ON ReviewSubmission WHEN OLD.InternalApprovalStampFinalized = 1 AND (NEW.InternalApprovalStampJson IS NOT OLD.InternalApprovalStampJson OR NEW.InternalApprovalStampFinalized IS NOT OLD.InternalApprovalStampFinalized) BEGIN INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,EventMetadataJson) VALUES (lower(hex(randomblob(16))), OLD.PackageId, 68, OLD.MakerUserId, CURRENT_TIMESTAMP, '{\"event\":\"internal-approval-stamp-mutation-rejected\"}', '{\"securityEvent\":true}'); SELECT RAISE(FAIL, 'Applied internal approval stamps are immutable.'); END; CREATE TRIGGER TR_ReviewSubmission_ApprovalStampDelete BEFORE DELETE ON ReviewSubmission WHEN OLD.InternalApprovalStampFinalized = 1 BEGIN SELECT RAISE(FAIL, 'Applied internal approval stamps cannot be removed.'); END;";
            stampGuards.ExecuteNonQuery();
        }
        using (var signatureGuards = c.CreateCommand())
        {
            signatureGuards.CommandText = "DROP TRIGGER IF EXISTS TR_ReviewSubmission_SignatureUpdate; DROP TRIGGER IF EXISTS TR_ReviewSubmission_SignatureDelete; CREATE TRIGGER TR_ReviewSubmission_SignatureUpdate BEFORE UPDATE OF MakerSignatureJson,MakerSignatureFinalized,CheckerSignatureJson,CheckerSignatureFinalized,ApproverSignatureJson,ApproverSignatureFinalized ON ReviewSubmission WHEN (OLD.MakerSignatureFinalized = 1 AND (NEW.MakerSignatureJson IS NOT OLD.MakerSignatureJson OR NEW.MakerSignatureFinalized IS NOT OLD.MakerSignatureFinalized)) OR (OLD.CheckerSignatureFinalized = 1 AND (NEW.CheckerSignatureJson IS NOT OLD.CheckerSignatureJson OR NEW.CheckerSignatureFinalized IS NOT OLD.CheckerSignatureFinalized)) OR (OLD.ApproverSignatureFinalized = 1 AND (NEW.ApproverSignatureJson IS NOT OLD.ApproverSignatureJson OR NEW.ApproverSignatureFinalized IS NOT OLD.ApproverSignatureFinalized)) BEGIN SELECT RAISE(FAIL, 'Finalized signatures are immutable.'); END; CREATE TRIGGER TR_ReviewSubmission_SignatureDelete BEFORE DELETE ON ReviewSubmission WHEN OLD.MakerSignatureFinalized = 1 OR OLD.CheckerSignatureFinalized = 1 OR OLD.ApproverSignatureFinalized = 1 BEGIN SELECT RAISE(FAIL, 'Finalized signatures cannot be removed.'); END;";
            signatureGuards.ExecuteNonQuery();
        }
        using (var fingerprintGuards = c.CreateCommand())
        {
            fingerprintGuards.CommandText = "DROP TRIGGER IF EXISTS TR_ReviewSubmission_FingerprintUpdate; DROP TRIGGER IF EXISTS TR_ReviewSubmission_FingerprintDelete; CREATE TRIGGER TR_ReviewSubmission_FingerprintUpdate BEFORE UPDATE OF FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion ON ReviewSubmission WHEN OLD.FingerprintJson IS NOT NULL AND (NEW.FingerprintJson IS NOT OLD.FingerprintJson OR NEW.FingerprintAlgorithm IS NOT OLD.FingerprintAlgorithm OR NEW.FingerprintCanonicalizationVersion IS NOT OLD.FingerprintCanonicalizationVersion) AND NOT ((OLD.CheckerSignatureFinalized IS NULL AND NEW.CheckerSignatureFinalized = 1 AND OLD.CheckerSignatureJson IS NULL AND NEW.CheckerSignatureJson IS NOT NULL) OR (OLD.ApproverSignatureFinalized IS NULL AND NEW.ApproverSignatureFinalized = 1 AND OLD.ApproverSignatureJson IS NULL AND NEW.ApproverSignatureJson IS NOT NULL) OR (OLD.InternalApprovalStampFinalized IS NULL AND NEW.InternalApprovalStampFinalized = 1 AND OLD.InternalApprovalStampJson IS NULL AND NEW.InternalApprovalStampJson IS NOT NULL)) BEGIN INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,EventMetadataJson) VALUES (lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))), OLD.PackageId, 34, OLD.MakerUserId, CURRENT_TIMESTAMP, '{\"event\":\"fingerprint-mutation-rejected\",\"operation\":\"UPDATE\",\"submissionId\":\"' || OLD.SubmissionId || '\",\"revision\":' || OLD.Revision || '}', '{\"securityEvent\":true}'); SELECT RAISE(FAIL, 'Submission fingerprints are immutable.'); END; CREATE TRIGGER TR_ReviewSubmission_FingerprintDelete BEFORE DELETE ON ReviewSubmission WHEN OLD.FingerprintJson IS NOT NULL BEGIN INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,EventMetadataJson) VALUES (lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))), OLD.PackageId, 34, OLD.MakerUserId, CURRENT_TIMESTAMP, '{\"event\":\"fingerprint-mutation-rejected\",\"operation\":\"DELETE\",\"submissionId\":\"' || OLD.SubmissionId || '\",\"revision\":' || OLD.Revision || '}', '{\"securityEvent\":true}'); SELECT RAISE(FAIL, 'Submission fingerprints are immutable.'); END;";
            fingerprintGuards.ExecuteNonQuery();
        }
        foreach (var table in new[] { "ReviewDecision", "FinalApprovalDecision" })
        foreach (var column in new[] { "ApproverUserId TEXT NULL", "ApproverDisplayName TEXT NULL", "IsFinalApproval INTEGER NOT NULL DEFAULT 0" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        foreach (var table in new[] { "ReviewDecision", "FinalApprovalDecision" })
        foreach (var column in new[] { "OverallReason TEXT NULL", "CorrectionAction TEXT NULL", "PriorDecisionId TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        foreach (var table in new[] { "ReviewDecision", "FinalApprovalDecision" })
        foreach (var column in new[] { "Route INTEGER NOT NULL DEFAULT 0", "DestinationRole TEXT NULL", "DestinationQueue TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
        MigrateCheckerDecisionUniqueness(c);
        foreach (var column in new[] { "IsDraft INTEGER NOT NULL DEFAULT 1", "ReferenceType TEXT NULL", "ReferenceId TEXT NULL", "Location TEXT NULL", "CorrectionResponse TEXT NULL", "PriorCommentId TEXT NULL" })
        { try { using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE ReviewComment ADD COLUMN {column};"; alter.ExecuteNonQuery(); } catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
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
        var submissions = await GetSubmissionsAsync(Guid.Empty, token).ConfigureAwait(false); return submissions.Where(x => x.Status is WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview or WorkflowStatus.ReturnedForRework).Where(x => x.AvailableToCheckers || string.Equals(x.AssignedCheckerUserId, reviewerUserId, StringComparison.OrdinalIgnoreCase)).Select(x => new WorkflowQueueItem(x.PackageId, x.Revision, x.MakerDisplayName, x.SubmittedUtc, x.Status, x.VerificationEvidenceJson) { AssignedCheckerUserId = x.AssignedCheckerUserId, AvailableToCheckers = x.AvailableToCheckers }).ToArray();
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
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM (SELECT DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision) ORDER BY DecidedUtc";
        cmd.Parameters.AddWithValue("$package", packageId.ToString()); cmd.Parameters.AddWithValue("$revision", revision);
        await using var reader = await cmd.ExecuteReaderAsync(token); var result = new List<ReviewDecision>();
        while (await reader.ReadAsync(token)) result.Add(MapDecision(reader, packageId, includePackageId: true));
        return result;
    }

    public async Task<ReviewPackage?> GetReviewPackageAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        var submissions = await GetSubmissionsAsync(packageId, token).ConfigureAwait(false);
        var submission = submissions.FirstOrDefault(x => x.Revision == revision);
        if (submission is null) return null;
        var decision = await GetDecisionAsync(packageId, revision, token).ConfigureAwait(false) ?? await GetLatestPriorDecisionAsync(packageId, revision, token).ConfigureAwait(false);
        var comments = await GetReviewCommentsAsync(packageId, revision, token).ConfigureAwait(false);
        if (revision > 1)
        {
            var priorComments = await GetReviewCommentsAsync(packageId, revision - 1, token).ConfigureAwait(false);
            var priorByReference = priorComments.Where(x => !string.IsNullOrWhiteSpace(x.ReferenceId)).GroupBy(x => x.ReferenceId!, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            comments = comments.Select(comment => comment.PriorCommentId is null && !string.IsNullOrWhiteSpace(comment.ReferenceId) && priorByReference.TryGetValue(comment.ReferenceId, out var prior) ? comment with { PriorCommentId = prior.CommentId } : comment).ToArray();
            comments = priorComments.Concat(comments).GroupBy(x => x.CommentId).Select(x => x.First()).ToArray();
        }
        var markups = await GetDocumentMarkupsAsync(packageId, submission.StepDocumentVersionId, token).ConfigureAwait(false);
        return ReviewPackage.FromSubmission(submission, decision, comments, markups);
    }

    public async Task<InternalApprovalStamp?> GetInternalApprovalStampAsync(Guid packageId, int revision, CancellationToken token = default)
        => (await GetSubmissionsAsync(packageId, token).ConfigureAwait(false)).FirstOrDefault(x => x.Revision == revision)?.InternalApprovalStamp;

    private async Task<ReviewDecision?> GetLatestPriorDecisionAsync(Guid packageId, int revision, CancellationToken token)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId FROM (SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId FROM ReviewDecision WHERE PackageId=$package AND Revision<$revision UNION ALL SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId FROM FinalApprovalDecision WHERE PackageId=$package AND Revision<$revision) ORDER BY Revision DESC, DecidedUtc DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$package", packageId.ToString()); cmd.Parameters.AddWithValue("$revision", revision);
        await using var r = await cmd.ExecuteReaderAsync(token); if (!await r.ReadAsync(token)) return null;
        return MapDecision(r, packageId, includePackageId: false);
    }

    public async Task RecordReviewStartedAsync(ReviewPackage package, AuditEntry auditEntry, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try { await Exec(c,t,"UPDATE WorkflowState SET Status=$underReview,UpdatedUtc=$updated WHERE PackageId=$package AND Revision=$revision AND Status=$submitted",token,("$underReview",(int)WorkflowStatus.UnderReview),("$updated",auditEntry.TimestampUtc.ToString("O",CultureInfo.InvariantCulture)),("$package",package.PackageId.ToString()),("$revision",package.Revision),("$submitted",(int)WorkflowStatus.SubmittedForReview)); await Exec(c,t,"UPDATE ReviewSubmission SET Status=$underReview WHERE PackageId=$package AND Revision=$revision AND Status=$submitted",token,("$underReview",(int)WorkflowStatus.UnderReview),("$package",package.PackageId.ToString()),("$revision",package.Revision),("$submitted",(int)WorkflowStatus.SubmittedForReview)); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; }
    }

    public async Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT CommentId,PackageId,Revision,AuthorUserId,AuthorDisplayName,CreatedUtc,Text,IsDraft,ReferenceType,ReferenceId,Location,CorrectionResponse,PriorCommentId FROM ReviewComment WHERE PackageId=$package AND Revision=$revision ORDER BY CreatedUtc"; cmd.Parameters.AddWithValue("$package", packageId.ToString()); cmd.Parameters.AddWithValue("$revision", revision); await using var r = await cmd.ExecuteReaderAsync(token); var result = new List<ReviewComment>(); while (await r.ReadAsync(token)) result.Add(new ReviewComment { CommentId=Guid.Parse(r.GetString(0)), PackageId=Guid.Parse(r.GetString(1)), Revision=r.GetInt32(2), AuthorUserId=r.GetString(3), AuthorDisplayName=r.GetString(4), CreatedUtc=DateTime.Parse(r.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Text=r.GetString(6), IsDraft=!r.IsDBNull(7) && r.GetInt32(7) != 0, ReferenceType=r.IsDBNull(8) ? null : r.GetString(8), ReferenceId=r.IsDBNull(9) ? null : r.GetString(9), Location=r.IsDBNull(10) ? null : r.GetString(10), CorrectionResponse=r.IsDBNull(11) ? null : r.GetString(11), PriorCommentId=r.IsDBNull(12) ? null : Guid.Parse(r.GetString(12)) }); return result;
    }

    public async Task SaveReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken token = default)
    {
        comment.Validate();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token); try { await Exec(c,t,"INSERT INTO ReviewComment(CommentId,PackageId,Revision,AuthorUserId,AuthorDisplayName,CreatedUtc,Text,IsDraft,ReferenceType,ReferenceId,Location,CorrectionResponse,PriorCommentId) VALUES($id,$package,$revision,$user,$display,$created,$text,$draft,$referenceType,$referenceId,$location,$response,$prior)",token,("$id",comment.CommentId.ToString()),("$package",comment.PackageId.ToString()),("$revision",comment.Revision),("$user",comment.AuthorUserId),("$display",comment.AuthorDisplayName),("$created",comment.CreatedUtc.ToString("O",CultureInfo.InvariantCulture)),("$text",comment.Text),("$draft",comment.IsDraft ? 1 : 0),("$referenceType",(object?)comment.ReferenceType ?? DBNull.Value),("$referenceId",(object?)comment.ReferenceId ?? DBNull.Value),("$location",(object?)comment.Location ?? DBNull.Value),("$response",(object?)comment.CorrectionResponse ?? DBNull.Value),("$prior",(object?)comment.PriorCommentId?.ToString() ?? DBNull.Value)); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; }
    }

    public async Task UpdateReviewCommentAsync(ReviewComment comment, AuditEntry auditEntry, CancellationToken token = default)
    {
        comment.Validate();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try { var changed = await Exec(c,t,"UPDATE ReviewComment SET Text=$text,IsDraft=$draft,ReferenceType=$referenceType,ReferenceId=$referenceId,Location=$location,CorrectionResponse=$response WHERE CommentId=$id AND PackageId=$package AND Revision=$revision AND IsDraft=1 AND Revision=(SELECT Revision FROM WorkflowState WHERE PackageId=$package) AND NOT EXISTS (SELECT 1 FROM RevisionLock WHERE PackageId=$package AND Revision=$revision)",token,("$text",comment.Text),("$draft",comment.IsDraft ? 1 : 0),("$referenceType",(object?)comment.ReferenceType ?? DBNull.Value),("$referenceId",(object?)comment.ReferenceId ?? DBNull.Value),("$location",(object?)comment.Location ?? DBNull.Value),("$response",(object?)comment.CorrectionResponse ?? DBNull.Value),("$id",comment.CommentId.ToString()),("$package",comment.PackageId.ToString()),("$revision",comment.Revision)); if (changed != 1) throw new InvalidOperationException("Only an unlocked comment in the current review revision can be edited."); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); }
        catch { await t.RollbackAsync(token); throw; }
    }

    public async Task DiscardReviewCommentAsync(Guid commentId, AuditEntry auditEntry, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try { var changed = await Exec(c,t,"DELETE FROM ReviewComment AS comment WHERE comment.CommentId=$id AND comment.IsDraft=1 AND comment.Revision=COALESCE((SELECT Revision FROM WorkflowState WHERE PackageId=comment.PackageId),(SELECT MAX(Revision) FROM ReviewSubmission WHERE PackageId=comment.PackageId)) AND NOT EXISTS (SELECT 1 FROM RevisionLock WHERE PackageId=comment.PackageId AND Revision=comment.Revision)",token,("$id",commentId.ToString())); if (changed != 1) throw new InvalidOperationException("Only an unlocked comment in the current review revision can be discarded."); await InsertAudit(c,t,auditEntry,token); await t.CommitAsync(token); }
        catch { await t.RollbackAsync(token); throw; }
    }

    public async Task<IReadOnlyList<DocumentMarkup>> GetDocumentMarkupsAsync(Guid documentId, Guid documentVersionId, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT MarkupId,DocumentId,DocumentVersionId,PackageId,Revision,AuthorUserId,AuthorDisplayName,CreatedUtc,MarkupType,DataJson,PageNumber,Location FROM DocumentMarkup WHERE DocumentVersionId=$version AND (DocumentId=$document OR PackageId=$document) ORDER BY CreatedUtc,MarkupId";
        cmd.Parameters.AddWithValue("$document", documentId.ToString()); cmd.Parameters.AddWithValue("$version", documentVersionId.ToString());
        await using var r = await cmd.ExecuteReaderAsync(token); var result = new List<DocumentMarkup>();
        while (await r.ReadAsync(token)) result.Add(new DocumentMarkup { MarkupId = Guid.Parse(r.GetString(0)), DocumentId = Guid.Parse(r.GetString(1)), DocumentVersionId = Guid.Parse(r.GetString(2)), PackageId = Guid.Parse(r.GetString(3)), Revision = r.GetInt32(4), AuthorUserId = r.GetString(5), AuthorDisplayName = r.GetString(6), CreatedUtc = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), MarkupType = r.GetString(8), DataJson = r.GetString(9), PageNumber = r.GetInt32(10), Location = r.IsDBNull(11) ? null : r.GetString(11) });
        return result;
    }

    public async Task SaveDocumentMarkupAsync(DocumentMarkup markup, AuditEntry auditEntry, CancellationToken token = default)
    {
        markup.Validate();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try { await Exec(c, t, "INSERT INTO DocumentMarkup(MarkupId,DocumentId,DocumentVersionId,PackageId,Revision,AuthorUserId,AuthorDisplayName,CreatedUtc,MarkupType,DataJson,PageNumber,Location) VALUES($id,$document,$version,$package,$revision,$user,$display,$created,$type,$data,$page,$location)", token, ("$id", markup.MarkupId.ToString()), ("$document", markup.DocumentId.ToString()), ("$version", markup.DocumentVersionId.ToString()), ("$package", markup.PackageId.ToString()), ("$revision", markup.Revision), ("$user", markup.AuthorUserId), ("$display", markup.AuthorDisplayName), ("$created", markup.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$type", markup.MarkupType), ("$data", markup.DataJson), ("$page", markup.PageNumber), ("$location", (object?)markup.Location ?? DBNull.Value)); await InsertAudit(c, t, auditEntry, token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; }
    }

    public Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, CancellationToken token = default)
        => SaveReviewDecisionAsync(package, decision, resultingStatus, auditEntry, null, token);

    public async Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus resultingStatus, AuditEntry auditEntry, InternalApprovalStamp? stamp, CancellationToken token = default)
    {
        decision.Validate();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            await using var state = c.CreateCommand(); state.Transaction=t; state.CommandText="UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=$package AND Status IN ($submitted,$underReview,$returnedForRework,$approvedByChecker,$pendingFinal) AND Revision=$revision"; state.Parameters.AddWithValue("$status",(int)resultingStatus); state.Parameters.AddWithValue("$updated",decision.DecidedUtc.ToString("O",CultureInfo.InvariantCulture)); state.Parameters.AddWithValue("$package",decision.PackageId.ToString()); state.Parameters.AddWithValue("$submitted",(int)WorkflowStatus.SubmittedForReview); state.Parameters.AddWithValue("$underReview",(int)WorkflowStatus.UnderReview); state.Parameters.AddWithValue("$returnedForRework",(int)WorkflowStatus.ReturnedForRework); state.Parameters.AddWithValue("$approvedByChecker",(int)WorkflowStatus.ApprovedByChecker); state.Parameters.AddWithValue("$pendingFinal",(int)WorkflowStatus.PendingFinalApproval); state.Parameters.AddWithValue("$revision",decision.Revision); if (await state.ExecuteNonQueryAsync(token) != 1) throw await CreateDecisionConflictAsync(c, t, decision.PackageId, decision.Revision, token);
            await Exec(c,t,"UPDATE ReviewSubmission SET Status=$status WHERE PackageId=$package AND Revision=$revision",token,("$status",(int)resultingStatus),("$package",decision.PackageId.ToString()),("$revision",decision.Revision));
            var stampAppliedUtc = auditEntry.TimestampUtc;
            string? stampAuditPayload = null;
            if (stamp is not null)
            {
                stamp.Validate();
                if (auditEntry.Action != DocumentAuditAction.InternalApprovalStampApplied) throw new InvalidOperationException("Internal approval stamp persistence requires an InternalApprovalStampApplied audit entry.");
                var stampId = stamp.StampId;
                stampAppliedUtc = stamp.AppliedUtc.UtcDateTime;
                stampAuditPayload = JsonSerializer.Serialize(new { stampId = stamp.StampId, stampAppliedUtc = stamp.AppliedUtc, stampApproverUserId = stamp.ApproverUserId, stampApproverDisplayName = stamp.ApproverDisplayName, stampApproverRole = stamp.ApproverRole, approverUserId = stamp.ApproverUserId, approverDisplayName = stamp.ApproverDisplayName, approverRole = stamp.ApproverRole, packageId = stamp.PackageId, revision = stamp.Revision, fingerprintReference = stamp.FingerprintReference, stampIntegrityHash = stamp.IntegrityHash, verificationStatus = stamp.VerificationStatus.ToString(), action = DocumentAuditAction.InternalApprovalStampApplied.ToString() });
                var stampedFingerprint = package.Fingerprint is null ? null : package.Fingerprint with { Value = SignatureData.ComputePayloadHash($"{package.Fingerprint.Value}|{stamp.CanonicalRepresentation}") };
                if (await Exec(c,t,"UPDATE ReviewSubmission SET InternalApprovalStampJson=$stamp,InternalApprovalStampFinalized=1,FingerprintJson=$fingerprint,FingerprintAlgorithm=$algorithm,FingerprintCanonicalizationVersion=$canonicalization WHERE PackageId=$package AND Revision=$revision AND InternalApprovalStampFinalized IS NULL",token,("$stamp",JsonSerializer.Serialize(stamp)),("$fingerprint",JsonSerializer.Serialize(stampedFingerprint)),("$algorithm",(object?)stampedFingerprint?.Algorithm??DBNull.Value),("$canonicalization",(object?)stampedFingerprint?.CanonicalizationVersion??DBNull.Value),("$package",decision.PackageId.ToString()),("$revision",decision.Revision)) != 1) throw new InvalidOperationException("The internal approval stamp could not be persisted for this document version.");
            }
            try
            {
                var table = decision.IsFinalApproval ? "FinalApprovalDecision" : "ReviewDecision";
                await Exec(c,t,$"INSERT INTO {table}(DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId) VALUES($id,$package,$revision,$user,$display,$time,$type,$comments,$reference,$snapshot,$approver,$approverDisplay,$final,$reason,$correction,$prior)",token,("$id",decision.DecisionId.ToString()),("$package",decision.PackageId.ToString()),("$revision",decision.Revision),("$user",decision.CheckerUserId),("$display",decision.CheckerDisplayName),("$time",decision.DecidedUtc.ToString("O",CultureInfo.InvariantCulture)),("$type",(int)decision.DecisionType),("$comments",(object?)decision.Comments??DBNull.Value),("$reference",decision.SnapshotReference),("$snapshot",decision.SnapshotJson),("$approver",(object?)decision.ApproverUserId??DBNull.Value),("$approverDisplay",(object?)decision.ApproverDisplayName??DBNull.Value),("$final",decision.IsFinalApproval ? 1 : 0),("$reason",(object?)decision.OverallReason??DBNull.Value),("$correction",(object?)decision.CorrectionAction??DBNull.Value),("$prior",(object?)decision.PriorDecisionId?.ToString()??DBNull.Value));
                await Exec(c,t,$"UPDATE {table} SET Route=$route,DestinationRole=$destinationRole,DestinationQueue=$destinationQueue WHERE DecisionId=$id",token,("$route",(int)decision.Route),("$destinationRole",(object?)decision.DestinationRole??DBNull.Value),("$destinationQueue",(object?)decision.DestinationQueue??DBNull.Value),("$id",decision.DecisionId.ToString()));
            }
            catch (SqliteException exception) when (IsReviewDecisionConflict(exception))
            {
                throw await CreateDecisionConflictAsync(c, t, decision.PackageId, decision.Revision, token);
            }
            if (decision.IsFinalApproval && resultingStatus == WorkflowStatus.FinallyApproved)
                await Exec(c, t, "INSERT INTO RevisionLock(PackageId,Revision,LockedUtc) VALUES($package,$revision,$locked)", token, ("$package", decision.PackageId.ToString()), ("$revision", decision.Revision), ("$locked", decision.DecidedUtc.ToString("O", CultureInfo.InvariantCulture)));
            if (stamp is null)
                await InsertAudit(c, t, auditEntry, token);
            else
                await InsertAudit(c, t, new AuditEntry { AuditId = auditEntry.AuditId, DocumentId = auditEntry.DocumentId, UserId = auditEntry.UserId, ActorRole = auditEntry.ActorRole, DocumentRevision = auditEntry.DocumentRevision, DocumentVersion = auditEntry.DocumentVersion, Action = DocumentAuditAction.InternalApprovalStampApplied, TimestampUtc = stampAppliedUtc, DetailsJson = stampAuditPayload! }, token);
            await t.CommitAsync(token); return decision;
        }
        catch (ReviewDecisionConflictException) { await t.RollbackAsync(token); throw; }
        catch (Exception exception) { await t.RollbackAsync(token); _logger?.Log(exception, "SQLite review decision persistence", $"Unable to persist decision for packageId={decision.PackageId}, revision={decision.Revision}."); throw; }
    }

    public async Task<ReviewSubmission> SaveCheckerSignatureAsync(ReviewPackage package, SignatureData signature, DocumentFingerprint fingerprint, AuditEntry auditEntry, CancellationToken token = default)
    {
        signature.Validate();
        if (!signature.IsChecker || signature.DocumentId != package.PackageId || signature.Revision != package.Revision) throw new InvalidOperationException("Checker signature does not match the current document version.");
        if (!fingerprint.IsValid) throw new InvalidOperationException("A valid fingerprint is required before a Checker signature can be persisted.");
        if (auditEntry.Action != DocumentAuditAction.CheckerSignatureApplied) throw new InvalidOperationException("Checker signature persistence requires a CheckerSignatureApplied audit entry.");
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            var assignments = MarkAssignmentSigned(package.Submission.PlaceholderAssignments, signature);
            var assignmentAware = package.Submission.PlaceholderAssignments.Count > 0;
            var changed = await Exec(c, t, "UPDATE ReviewSubmission SET CheckerSignatureJson=COALESCE(CheckerSignatureJson,$signature),CheckerSignatureFinalized=COALESCE(CheckerSignatureFinalized,1),FingerprintJson=CASE WHEN CheckerSignatureFinalized IS NULL THEN $fingerprint ELSE FingerprintJson END,FingerprintAlgorithm=CASE WHEN CheckerSignatureFinalized IS NULL THEN $algorithm ELSE FingerprintAlgorithm END,FingerprintCanonicalizationVersion=CASE WHEN CheckerSignatureFinalized IS NULL THEN $canonicalization ELSE FingerprintCanonicalizationVersion END,PlaceholderAssignmentsJson=$assignments WHERE PackageId=$package AND Revision=$revision AND (CheckerSignatureFinalized IS NULL OR $assignmentAware=1) AND NOT EXISTS (SELECT 1 FROM RevisionLock WHERE PackageId=$package AND Revision=$revision)", token, ("$signature", JsonSerializer.Serialize(signature)), ("$fingerprint", JsonSerializer.Serialize(fingerprint)), ("$algorithm", fingerprint.Algorithm), ("$canonicalization", fingerprint.CanonicalizationVersion), ("$assignments", (object?)JsonSerializer.Serialize(assignments) ?? DBNull.Value), ("$assignmentAware", assignmentAware ? 1 : 0), ("$package", package.PackageId.ToString()), ("$revision", package.Revision));
            if (changed != 1) throw new InvalidOperationException("A finalized Checker signature already exists for this document version.");
            await InsertAudit(c, t, auditEntry, token);
            await t.CommitAsync(token);
            var checkerSignature = signature;
            return package.Submission with { CheckerSignature = checkerSignature, Fingerprint = fingerprint, PlaceholderAssignments = assignments };
        }
        catch { await t.RollbackAsync(token); throw; }
    }

    public async Task<ReviewSubmission> SaveApproverSignatureAsync(ReviewPackage package, SignatureData signature, DocumentFingerprint fingerprint, AuditEntry auditEntry, CancellationToken token = default)
    {
        signature.Validate();
        if (!signature.IsApprover || signature.DocumentId != package.PackageId || signature.Revision != package.Revision) throw new InvalidOperationException("Approver signature does not match the current document version.");
        if (!fingerprint.IsValid) throw new InvalidOperationException("A valid fingerprint is required before an Approver signature can be persisted.");
        if (auditEntry.Action != DocumentAuditAction.ApproverSignatureApplied) throw new InvalidOperationException("Approver signature persistence requires an ApproverSignatureApplied audit entry.");
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token);
        try
        {
            var assignments = MarkAssignmentSigned(package.Submission.PlaceholderAssignments, signature);
            var assignmentAware = package.Submission.PlaceholderAssignments.Count > 0;
            var changed = await Exec(c, t, "UPDATE ReviewSubmission SET ApproverSignatureJson=COALESCE(ApproverSignatureJson,$signature),ApproverSignatureFinalized=COALESCE(ApproverSignatureFinalized,1),FingerprintJson=CASE WHEN ApproverSignatureFinalized IS NULL THEN $fingerprint ELSE FingerprintJson END,FingerprintAlgorithm=CASE WHEN ApproverSignatureFinalized IS NULL THEN $algorithm ELSE FingerprintAlgorithm END,FingerprintCanonicalizationVersion=CASE WHEN ApproverSignatureFinalized IS NULL THEN $canonicalization ELSE FingerprintCanonicalizationVersion END,PlaceholderAssignmentsJson=$assignments WHERE PackageId=$package AND Revision=$revision AND (ApproverSignatureFinalized IS NULL OR $assignmentAware=1) AND NOT EXISTS (SELECT 1 FROM RevisionLock WHERE PackageId=$package AND Revision=$revision)", token, ("$signature", JsonSerializer.Serialize(signature)), ("$fingerprint", JsonSerializer.Serialize(fingerprint)), ("$algorithm", fingerprint.Algorithm), ("$canonicalization", fingerprint.CanonicalizationVersion), ("$assignments", (object?)JsonSerializer.Serialize(assignments) ?? DBNull.Value), ("$assignmentAware", assignmentAware ? 1 : 0), ("$package", package.PackageId.ToString()), ("$revision", package.Revision));
            if (changed != 1) throw new InvalidOperationException("A finalized Approver signature already exists for this document version.");
            await InsertAudit(c, t, auditEntry, token); await t.CommitAsync(token);
            return package.Submission with { ApproverSignature = package.Submission.ApproverSignature ?? signature, Fingerprint = fingerprint, PlaceholderAssignments = assignments };
        }
        catch { await t.RollbackAsync(token); throw; }
    }

    private static bool IsReviewDecisionConflict(SqliteException exception)
        => exception.SqliteErrorCode == 19 && (exception.Message.Contains("ReviewDecision.PackageId", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("FinalApprovalDecision.PackageId", StringComparison.OrdinalIgnoreCase)) && (exception.Message.Contains("ReviewDecision.Revision", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("FinalApprovalDecision.Revision", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SignaturePlaceholderAssignment> MarkAssignmentSigned(IReadOnlyList<SignaturePlaceholderAssignment> assignments, SignatureData signature)
    {
        var roleAssignments = assignments.Where(x => string.Equals(x.Role, signature.Role, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roleAssignments.Length == 0) return assignments;
        if (signature.PlaceholderId is null) throw new InvalidOperationException("A placeholder target is required when assignments are configured for this signature role.");
        if (signature.PlaceholderId is not { } placeholderId) return assignments;
        var assignment = roleAssignments.SingleOrDefault(x => x.PlaceholderId == placeholderId) ?? throw new InvalidOperationException("The signature placeholder assignment was not found for this signature role.");
        if (assignment.IsSigned) throw new InvalidOperationException("The signature placeholder has already been signed.");
        return assignments.Select(x => x.PlaceholderId == placeholderId ? x with { IsSigned = true, SignedByUserId = signature.SignerUserId, SignedUtc = signature.TimestampUtc } : x).ToArray();
    }

    private static void MigrateCheckerDecisionUniqueness(SqliteConnection c)
    {
        var hasCheckerRevisionUniqueIndex = false;
        var uniqueIndexes = new List<string>();
        using (var indexes = c.CreateCommand())
        {
            indexes.CommandText = "PRAGMA index_list('ReviewDecision');";
            using var reader = indexes.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt32(2) == 0) continue;
                uniqueIndexes.Add(reader.GetString(1));
            }
        }
        foreach (var uniqueIndex in uniqueIndexes)
        {
                var indexName = uniqueIndex.Replace("'", "''", StringComparison.Ordinal);
                using var columns = c.CreateCommand();
                columns.CommandText = $"PRAGMA index_info('{indexName}');";
                using var columnReader = columns.ExecuteReader();
                var names = new List<string>();
                while (columnReader.Read()) names.Add(columnReader.GetString(2));
                if (names.SequenceEqual(new[] { "PackageId", "Revision" }, StringComparer.OrdinalIgnoreCase))
                {
                    hasCheckerRevisionUniqueIndex = true;
                    break;
                }
        }
        if (!hasCheckerRevisionUniqueIndex) return;

        using var transaction = c.BeginTransaction();
        try
        {
            ExecuteSchemaCommand(c, transaction, "ALTER TABLE ReviewDecision RENAME TO ReviewDecision_Legacy;");
            ExecuteSchemaCommand(c, transaction, "CREATE TABLE ReviewDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, CheckerUserId TEXT NOT NULL, CheckerDisplayName TEXT NOT NULL, DecidedUtc TEXT NOT NULL, DecisionType INTEGER NOT NULL, Comments TEXT NULL, SnapshotReference TEXT NOT NULL, SnapshotJson TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 0, OverallReason TEXT NULL, CorrectionAction TEXT NULL, PriorDecisionId TEXT NULL, Route INTEGER NOT NULL DEFAULT 0, DestinationRole TEXT NULL, DestinationQueue TEXT NULL);");
            ExecuteSchemaCommand(c, transaction, "INSERT INTO ReviewDecision(DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue) SELECT DecisionId,PackageId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM ReviewDecision_Legacy;");
            ExecuteSchemaCommand(c, transaction, "DROP TABLE ReviewDecision_Legacy;");
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void ExecuteSchemaCommand(SqliteConnection connection, SqliteTransaction transaction, string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

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
            decision.Transaction = transaction; decision.CommandText = "SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM (SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision) LIMIT 1"; decision.Parameters.AddWithValue("$package", packageId.ToString()); decision.Parameters.AddWithValue("$revision", requestedRevision);
            await using var reader = await decision.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) prior = MapDecision(reader, packageId, includePackageId: false);
        }
        return new ReviewDecisionConflictException(packageId, requestedRevision, status, revision, prior);
    }

    private async Task<ReviewDecision?> GetDecisionAsync(Guid packageId, int revision, CancellationToken token)
    {
        await using var c=new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM (SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM FinalApprovalDecision WHERE PackageId=$package AND Revision=$revision UNION ALL SELECT DecisionId,Revision,CheckerUserId,CheckerDisplayName,DecidedUtc,DecisionType,Comments,SnapshotReference,SnapshotJson,ApproverUserId,ApproverDisplayName,IsFinalApproval,OverallReason,CorrectionAction,PriorDecisionId,Route,DestinationRole,DestinationQueue FROM ReviewDecision WHERE PackageId=$package AND Revision=$revision) LIMIT 1"; cmd.Parameters.AddWithValue("$package",packageId.ToString()); cmd.Parameters.AddWithValue("$revision",revision); await using var r=await cmd.ExecuteReaderAsync(token); if(!await r.ReadAsync(token)) return null; return MapDecision(r, packageId, includePackageId: false);
    }
    public async Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expected, ReviewSubmission submission, AuditEntry audit, CancellationToken token = default)
    {
        var effectiveVersion = _configurationRepository is null
            ? (int?)null
            : (await _configurationRepository.GetCurrentAsync(token).ConfigureAwait(false))?.Version
                ?? throw new InvalidOperationException("No effective workflow configuration is available for workflow start.");
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
            if (!stateExists && effectiveVersion is not null)
                await Exec(c, t, "INSERT INTO WorkflowInstances(WorkflowId,ConfigurationVersion,StartedUtc) VALUES($workflow,$version,$started)", token, ("$workflow", expected.PackageId.ToString()), ("$version", effectiveVersion.Value), ("$started", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)));
            if (submission.DxfDocumentId != Guid.Empty && (submission.Fingerprint is null || !submission.Fingerprint.IsValid)) throw new InvalidOperationException("A valid SHA-256 fingerprint with supported metadata is required before a submission can be persisted.");
            await Exec(c, t, "INSERT INTO ReviewSubmission(SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,CreatedUtc,SubmittedUtc,Note,Status,AssignedCheckerUserId,AssignedCheckerUserIdsJson,AssignedApproverUserId,AssignedApproverUserIdsJson,FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion,MakerSignatureJson,MakerSignatureFinalized,TemplateMappingJson,PlaceholderAssignmentsJson) VALUES($id,$package,$dxfDocument,$step,$dxf,$session,$extract,$verify,$revision,$maker,$display,$created,$submitted,$note,$status,$checker,$checkers,$approver,$approvers,$fingerprint,$algorithm,$canonicalization,$signature,$signatureFinalized,$mapping,$assignments)", token, ("$id", submission.SubmissionId.ToString()), ("$package", submission.PackageId.ToString()), ("$dxfDocument", submission.DxfDocumentId.ToString()), ("$step", submission.StepDocumentVersionId.ToString()), ("$dxf", submission.DxfDocumentVersionId.ToString()), ("$session", submission.VerificationSessionId.ToString()), ("$extract", submission.ExtractionEvidenceJson), ("$verify", submission.VerificationEvidenceJson), ("$revision", submission.Revision), ("$maker", submission.MakerUserId), ("$display", submission.MakerDisplayName), ("$created", submission.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$submitted", submission.SubmittedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$note", (object?)submission.Note ?? DBNull.Value), ("$status", (int)submission.Status), ("$checker", (object?)submission.AssignedCheckerUserId ?? DBNull.Value), ("$checkers", JsonSerializer.Serialize(submission.AssignedCheckerUserIds)), ("$approver", (object?)submission.AssignedApproverUserId ?? DBNull.Value), ("$approvers", JsonSerializer.Serialize(submission.AssignedApproverUserIds)), ("$fingerprint", (object?)JsonSerializer.Serialize(submission.Fingerprint) ?? DBNull.Value), ("$algorithm", (object?)submission.Fingerprint?.Algorithm ?? DBNull.Value), ("$canonicalization", (object?)submission.Fingerprint?.CanonicalizationVersion ?? DBNull.Value), ("$signature", (object?)(submission.MakerSignature is null ? null : JsonSerializer.Serialize(submission.MakerSignature)) ?? DBNull.Value), ("$signatureFinalized", (object?)(submission.MakerSignature?.IsFinalized == true ? 1 : null) ?? DBNull.Value), ("$mapping", (object?)(submission.TemplateMapping is null ? null : JsonSerializer.Serialize(submission.TemplateMapping)) ?? DBNull.Value), ("$assignments", (object?)(submission.PlaceholderAssignments.Count == 0 ? null : JsonSerializer.Serialize(submission.PlaceholderAssignments)) ?? DBNull.Value));
            await InsertAudit(c, t, audit, token); await t.CommitAsync(token); return submission;
        }
        catch { await t.RollbackAsync(token); throw; }
    }
    public async Task RecordAuditAsync(AuditEntry entry, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await InsertAudit(c, null, entry, token); }
    public async Task<FingerprintVerificationResult?> GetFingerprintVerificationAsync(Guid packageId, int revision, CancellationToken token = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        await using var command = c.CreateCommand();
        command.CommandText = "SELECT TimestampUtc,DetailsJson FROM DocumentAuditLog WHERE DocumentId=$document AND DocumentRevision=$revision AND Action IN ($completed,$failed,$integrity) ORDER BY TimestampUtc DESC,AuditId DESC LIMIT 1";
        command.Parameters.AddWithValue("$document", packageId.ToString()); command.Parameters.AddWithValue("$revision", revision.ToString()); command.Parameters.AddWithValue("$completed", (int)DocumentAuditAction.VerificationRunCompleted); command.Parameters.AddWithValue("$failed", (int)DocumentAuditAction.VerificationRunFailed); command.Parameters.AddWithValue("$integrity", (int)DocumentAuditAction.IntegrityCheckFailed);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var timestampText = reader.GetString(0); var value = reader.GetString(1);
        using var json = JsonDocument.Parse(value); var root = json.RootElement;
        var statusText = root.TryGetProperty("result", out var status) ? status.GetString() : null;
        if (!Enum.TryParse<FingerprintVerificationStatus>(statusText, true, out var parsed)) parsed = FingerprintVerificationStatus.Error;
        static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
        var stored = Text(root, "storedFingerprint"); var recomputed = Text(root, "recomputedFingerprint");
        var canonicalizationVersion = root.TryGetProperty("fingerprintCanonicalizationVersion", out var version) && version.TryGetInt32(out var number) ? number : 1;
        return new FingerprintVerificationResult(parsed, string.IsNullOrWhiteSpace(recomputed) ? null : new DocumentFingerprint { Value = recomputed, Algorithm = Text(root, "fingerprintAlgorithm") ?? "SHA-256", CanonicalizationVersion = canonicalizationVersion }, parsed switch { FingerprintVerificationStatus.Match => "Fingerprint Verified - Document Unchanged", FingerprintVerificationStatus.Mismatch => "Fingerprint Mismatch - Document May Have Been Altered", _ => "Cannot verify fingerprint - file(s) missing or corrupted" }) { Stored = string.IsNullOrWhiteSpace(stored) ? null : new DocumentFingerprint { Value = stored, Algorithm = Text(root, "fingerprintAlgorithm") ?? "SHA-256", CanonicalizationVersion = canonicalizationVersion }, VerifiedUtc = DateTimeOffset.Parse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), VerifierUserId = Text(root, "checkerUserId"), VerifierDisplayName = Text(root, "checkerDisplayName"), DocumentId = packageId, Revision = revision, ErrorDetails = Text(root, "error") };
    }
    public async Task MarkSupersededAsync(Guid submissionId, AuditEntry audit, CancellationToken token = default) { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var t = (SqliteTransaction)await c.BeginTransactionAsync(token); try { await Exec(c,t,"UPDATE ReviewSubmission SET Status=$status WHERE SubmissionId=$id",token,("$status",(int)WorkflowStatus.Superseded),("$id",submissionId.ToString())); await Exec(c,t,"UPDATE WorkflowState SET Status=$status,UpdatedUtc=$updated WHERE PackageId=(SELECT PackageId FROM ReviewSubmission WHERE SubmissionId=$id)",token,("$status",(int)WorkflowStatus.Superseded),("$updated",audit.TimestampUtc.ToString("O",CultureInfo.InvariantCulture)),("$id",submissionId.ToString())); await InsertAudit(c,t,audit,token); await t.CommitAsync(token); } catch { await t.RollbackAsync(token); throw; } }
    private const string SelectSubmission = "SELECT SubmissionId,PackageId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,VerificationSessionId,ExtractionEvidenceJson,VerificationEvidenceJson,Revision,MakerUserId,MakerDisplayName,CreatedUtc,SubmittedUtc,Note,Status,AssignedCheckerUserId,AvailableToCheckers,AssignedApproverUserId,AvailableToApprovers,FingerprintJson,FingerprintAlgorithm,FingerprintCanonicalizationVersion,MakerSignatureJson,MakerSignatureFinalized,CheckerSignatureJson,CheckerSignatureFinalized,ApproverSignatureJson,ApproverSignatureFinalized,InternalApprovalStampJson,InternalApprovalStampFinalized,AssignedCheckerUserIdsJson,AssignedApproverUserIdsJson,TemplateMappingJson,PlaceholderAssignmentsJson FROM ReviewSubmission";
    private static WorkflowState MapState(SqliteDataReader r) => new() { PackageId = Guid.Parse(r.GetString(0)), Status = (WorkflowStatus)r.GetInt32(1), Revision = r.GetInt32(2), UpdatedUtc = DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
    private static ReviewSubmission MapSubmission(SqliteDataReader r)
    {
        var submitted = DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var packageId = Guid.Parse(r.GetString(1));
        var revision = r.GetInt32(8);
        var makerUserId = r.GetString(9);
        var makerDisplayName = r.GetString(10);
        var assignedCheckerUserId = r.IsDBNull(15) ? null : r.GetString(15);
        var assignedCheckerUserIds = r.IsDBNull(30)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(r.GetString(30))
                ?? throw new InvalidDataException("Stored Checker assignment data is invalid.");
        if (assignedCheckerUserIds.Length == 0 && !string.IsNullOrWhiteSpace(assignedCheckerUserId))
            assignedCheckerUserIds = new[] { assignedCheckerUserId };
        var fingerprint = r.IsDBNull(19) ? null : JsonSerializer.Deserialize<DocumentFingerprint>(r.GetString(19));
        if (fingerprint is not null && (!r.IsDBNull(20) || !r.IsDBNull(21))) fingerprint = fingerprint with { Algorithm = r.IsDBNull(20) ? fingerprint.Algorithm : r.GetString(20), CanonicalizationVersion = r.IsDBNull(21) ? fingerprint.CanonicalizationVersion : r.GetInt32(21) };
        SignatureData? signature = null; if (!r.IsDBNull(22)) { signature = JsonSerializer.Deserialize<SignatureData>(r.GetString(22)) ?? throw new InvalidDataException("Stored Maker signature data is invalid."); if (r.IsDBNull(23) || r.GetInt32(23) != 1) throw new InvalidDataException("Stored Maker signature is not finalized."); signature.Validate(); if (signature.DocumentId != packageId || signature.Revision != revision || !string.Equals(signature.MakerUserId, makerUserId, StringComparison.Ordinal) || !string.Equals(signature.MakerDisplayName, makerDisplayName, StringComparison.Ordinal)) throw new InvalidDataException("Stored Maker signature identity or revision does not match its submission."); } SignatureData? checkerSignature = null; if (r.FieldCount > 24 && !r.IsDBNull(24)) { checkerSignature = JsonSerializer.Deserialize<SignatureData>(r.GetString(24)) ?? throw new InvalidDataException("Stored Checker signature data is invalid."); if (r.IsDBNull(25) || r.GetInt32(25) != 1) throw new InvalidDataException("Stored Checker signature is not finalized."); checkerSignature.Validate(); if (checkerSignature.DocumentId != packageId || checkerSignature.Revision != revision) throw new InvalidDataException("Stored Checker signature identity or revision does not match its submission."); } SignatureData? approverSignature = null; if (r.FieldCount > 26 && !r.IsDBNull(26)) { approverSignature = JsonSerializer.Deserialize<SignatureData>(r.GetString(26)) ?? throw new InvalidDataException("Stored Approver signature data is invalid."); if (r.IsDBNull(27) || r.GetInt32(27) != 1) throw new InvalidDataException("Stored Approver signature is not finalized."); approverSignature.Validate(); if (approverSignature.DocumentId != packageId || approverSignature.Revision != revision) throw new InvalidDataException("Stored Approver signature identity or revision does not match its submission."); } InternalApprovalStamp? stamp = null; if (r.FieldCount > 28 && !r.IsDBNull(28)) { stamp = JsonSerializer.Deserialize<InternalApprovalStamp>(r.GetString(28)) ?? throw new InvalidDataException("Stored internal approval stamp data is invalid."); if (r.IsDBNull(29) || r.GetInt32(29) != 1) throw new InvalidDataException("Stored internal approval stamp is not finalized."); stamp.Validate(); if (stamp.DocumentId != packageId || stamp.PackageId != packageId || stamp.Revision != revision) throw new InvalidDataException("Stored internal approval stamp identity or revision does not match its submission."); }
        var assignedApproverUserId = r.IsDBNull(17) ? null : r.GetString(17);
        var assignedApproverUserIds = r.IsDBNull(31) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(r.GetString(31)) ?? throw new InvalidDataException("Stored Approver assignment data is invalid.");
        if (assignedApproverUserIds.Length == 0 && !string.IsNullOrWhiteSpace(assignedApproverUserId)) assignedApproverUserIds = new[] { assignedApproverUserId };
        TemplateMappingSnapshot? templateMapping = r.FieldCount > 32 && !r.IsDBNull(32) ? JsonSerializer.Deserialize<TemplateMappingSnapshot>(r.GetString(32)) : null;
        var placeholderAssignments = r.FieldCount > 33 && !r.IsDBNull(33) ? JsonSerializer.Deserialize<SignaturePlaceholderAssignment[]>(r.GetString(33)) ?? throw new InvalidDataException("Stored placeholder assignment data is invalid.") : Array.Empty<SignaturePlaceholderAssignment>();
        foreach (var assignment in placeholderAssignments) assignment.Validate();
        return new() { SubmissionId=Guid.Parse(r.GetString(0)),PackageId=packageId,DxfDocumentId=Guid.TryParse(r.GetString(2), out var dxfDocumentId) ? dxfDocumentId : Guid.Empty,StepDocumentVersionId=Guid.Parse(r.GetString(3)),DxfDocumentVersionId=Guid.Parse(r.GetString(4)),VerificationSessionId=Guid.Parse(r.GetString(5)),ExtractionEvidenceJson=r.GetString(6),VerificationEvidenceJson=r.GetString(7),Revision=revision,MakerUserId=makerUserId,MakerDisplayName=makerDisplayName,CreatedUtc=r.IsDBNull(11) ? submitted : DateTime.Parse(r.GetString(11),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),SubmittedUtc=submitted,Note=r.IsDBNull(13)?null:r.GetString(13),Status=(WorkflowStatus)r.GetInt32(14),AssignedCheckerUserId=assignedCheckerUserId,AssignedCheckerUserIds=assignedCheckerUserIds,AvailableToCheckers=r.GetInt32(16) != 0,AssignedApproverUserId=assignedApproverUserId,AssignedApproverUserIds=assignedApproverUserIds,AvailableToApprovers=!r.IsDBNull(18) && r.GetInt32(18) != 0,Fingerprint=fingerprint,MakerSignature=signature,CheckerSignature=checkerSignature,ApproverSignature=approverSignature,InternalApprovalStamp=stamp,TemplateMapping=templateMapping,PlaceholderAssignments=placeholderAssignments };
    }
    private static ReviewDecision MapDecision(SqliteDataReader r, Guid packageId, bool includePackageId)
    {
        var offset = includePackageId ? 1 : 0;
        return new ReviewDecision
        {
            DecisionId = Guid.Parse(r.GetString(0)), PackageId = includePackageId ? Guid.Parse(r.GetString(1)) : packageId,
            Revision = r.GetInt32(offset + 1), CheckerUserId = r.GetString(offset + 2), CheckerDisplayName = r.GetString(offset + 3),
            DecidedUtc = DateTime.Parse(r.GetString(offset + 4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DecisionType = (ReviewDecisionType)r.GetInt32(offset + 5), Comments = r.IsDBNull(offset + 6) ? null : r.GetString(offset + 6),
            SnapshotReference = r.GetString(offset + 7), SnapshotJson = r.GetString(offset + 8),
            ApproverUserId = r.IsDBNull(offset + 9) ? string.Empty : r.GetString(offset + 9), ApproverDisplayName = r.IsDBNull(offset + 10) ? string.Empty : r.GetString(offset + 10),
            IsFinalApproval = !r.IsDBNull(offset + 11) && r.GetInt32(offset + 11) != 0,
            OverallReason = r.IsDBNull(offset + 12) ? null : r.GetString(offset + 12), CorrectionAction = r.IsDBNull(offset + 13) ? null : r.GetString(offset + 13),
            PriorDecisionId = r.IsDBNull(offset + 14) ? null : Guid.Parse(r.GetString(offset + 14)), Route = (ReviewDecisionRoute)r.GetInt32(offset + 15),
            DestinationRole = r.IsDBNull(offset + 16) ? null : r.GetString(offset + 16), DestinationQueue = r.IsDBNull(offset + 17) ? null : r.GetString(offset + 17)
        };
    }
    private static async Task InsertAudit(SqliteConnection c, SqliteTransaction? t, AuditEntry e, CancellationToken token)
    {
        var timestamp = new DateTimeOffset(DateTime.SpecifyKind(e.TimestampUtc, DateTimeKind.Utc)); Guid? predecessor = e.PredecessorAuditId; string? hash = e.IntegrityHash;
        if (e.DocumentId.HasValue && hash is null)
        {
            await using var prior = c.CreateCommand(); prior.Transaction = t; prior.CommandText = "SELECT AuditId,IntegrityHash FROM DocumentAuditLog WHERE DocumentId=$document ORDER BY TimestampUtc DESC,AuditId DESC LIMIT 1"; prior.Parameters.AddWithValue("$document", e.DocumentId.Value.ToString());
            string? previousHash = null;
            await using (var reader = await prior.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token)) { predecessor = Guid.Parse(reader.GetString(0)); previousHash = reader.IsDBNull(1) ? null : reader.GetString(1); }
            }
            hash = Hash(previousHash ?? string.Empty, e.AuditId, e.DocumentId.Value, e.Action, e.UserId, timestamp, e.DocumentRevision, e.EventMetadataJson, e.DetailsJson);
        }
        await Exec(c,t,"INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,ActorRole,DocumentRevision,DocumentVersion,EventMetadataJson,PreviousWorkflowStatus,NewWorkflowStatus,PredecessorAuditId,IntegrityHash) VALUES($id,$document,$action,$user,$time,$details,$role,$revision,$version,$metadata,$previous,$next,$predecessor,$hash)",token,("$id",e.AuditId.ToString()),("$document",(object?)e.DocumentId?.ToString()??DBNull.Value),("$action",(int)e.Action),("$user",(object?)e.UserId??DBNull.Value),("$time",timestamp.UtcDateTime.ToString("O",CultureInfo.InvariantCulture)),("$details",e.DetailsJson),("$role",(object?)e.ActorRole??DBNull.Value),("$revision",(object?)e.DocumentRevision??DBNull.Value),("$version",(object?)e.DocumentVersion??DBNull.Value),("$metadata",e.EventMetadataJson),("$previous",(object?)e.PreviousWorkflowStatus??DBNull.Value),("$next",(object?)e.NewWorkflowStatus??DBNull.Value),("$predecessor",(object?)predecessor?.ToString()??DBNull.Value),("$hash",(object?)hash??DBNull.Value));
    }
    private static string Hash(string previous, Guid id, Guid document, DocumentAuditAction action, string actor, DateTimeOffset timestamp, string? revision, string metadata, string details) { var payload=$"{previous}|{id}|{document}|{action}|{actor}|{timestamp:O}|{revision}|{metadata}|{details}"; return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))); }
    private static async Task<int> Exec(SqliteConnection c, SqliteTransaction? t, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; foreach(var value in values) cmd.Parameters.AddWithValue(value.Name,value.Value); return await cmd.ExecuteNonQueryAsync(token); }
}
