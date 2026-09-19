using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteSharedDocumentRepository : ISharedDocumentRepository
{
    private readonly string _connectionString;
    public SqliteSharedDocumentRepository(string databasePath, ICentralizedLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        try { using var connection = new SqliteConnection(_connectionString); connection.Open(); EnsureSchema(connection); }
        catch (Exception exception) { logger?.Log(exception, "Shared document schema", "Unable to initialize explicit shared-document persistence."); throw; }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS SharedDocumentShare (DocumentId TEXT NOT NULL, VersionId TEXT NOT NULL, RecipientUserId TEXT NOT NULL, SharedByUserId TEXT NOT NULL, SharedByName TEXT NOT NULL, SharedUtc TEXT NOT NULL, Note TEXT NULL, IsRevoked INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(DocumentId,VersionId,RecipientUserId)); CREATE INDEX IF NOT EXISTS IX_SharedDocumentShare_Recipient ON SharedDocumentShare(RecipientUserId,IsRevoked); CREATE TABLE IF NOT EXISTS ReviewSubmission (SubmissionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL DEFAULT '', StepDocumentVersionId TEXT NOT NULL DEFAULT '', DxfDocumentVersionId TEXT NOT NULL DEFAULT '', VerificationSessionId TEXT NOT NULL DEFAULT '', ExtractionEvidenceJson TEXT NOT NULL DEFAULT '', VerificationEvidenceJson TEXT NOT NULL DEFAULT '', Revision INTEGER NOT NULL, MakerUserId TEXT NOT NULL DEFAULT '', MakerDisplayName TEXT NOT NULL DEFAULT '', CreatedUtc TEXT NULL, SubmittedUtc TEXT NOT NULL DEFAULT '', Note TEXT NULL, Status INTEGER NOT NULL DEFAULT 0, AssignedCheckerUserId TEXT NULL, AvailableToCheckers INTEGER NOT NULL DEFAULT 1, AssignedApproverUserId TEXT NULL, AvailableToApprovers INTEGER NOT NULL DEFAULT 1, FingerprintJson TEXT NULL, FingerprintAlgorithm TEXT NULL, FingerprintCanonicalizationVersion INTEGER NULL, MakerSignatureJson TEXT NULL, MakerSignatureFinalized INTEGER NULL, CheckerSignatureJson TEXT NULL, CheckerSignatureFinalized INTEGER NULL, ApproverSignatureJson TEXT NULL, ApproverSignatureFinalized INTEGER NULL, InternalApprovalStampJson TEXT NULL, InternalApprovalStampFinalized INTEGER NULL); CREATE INDEX IF NOT EXISTS IX_ReviewSubmission_Package ON ReviewSubmission(PackageId);";
        command.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<SharedDocument>> ListAsync(string recipientUserId, SharedDocumentQuery query, CancellationToken token = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = QuerySql("AND ($search IS NULL OR d.OriginalFileName LIKE $like OR CAST(v.VersionNumber AS TEXT) LIKE $like)");
        command.Parameters.AddWithValue("$recipient", recipientUserId); command.Parameters.AddWithValue("$search", (object?)query.NormalizedSearch ?? DBNull.Value); command.Parameters.AddWithValue("$like", (object?)(query.NormalizedSearch is null ? DBNull.Value : $"%{query.NormalizedSearch}%") ?? DBNull.Value);
        return await ReadAsync(command, token).ConfigureAwait(false);
    }

    public async Task<SharedDocument?> GetAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandText = QuerySql("AND d.DocumentId=$document AND ($version IS NULL OR v.VersionId=$version)");
        command.Parameters.AddWithValue("$recipient", recipientUserId); command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", (object?)versionId?.ToString() ?? DBNull.Value); command.Parameters.AddWithValue("$search", DBNull.Value); command.Parameters.AddWithValue("$like", DBNull.Value);
        var values = await ReadAsync(command, token).ConfigureAwait(false); return values.FirstOrDefault();
    }

    public async Task<SharedDocument?> GetAccessStateAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandText = QuerySql("AND d.DocumentId=$document AND ($version IS NULL OR v.VersionId=$version)", includeRevoked: true);
        command.Parameters.AddWithValue("$recipient", recipientUserId); command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", (object?)versionId?.ToString() ?? DBNull.Value); command.Parameters.AddWithValue("$search", DBNull.Value); command.Parameters.AddWithValue("$like", DBNull.Value);
        return (await ReadAsync(command, token).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task CreateShareAsync(Guid documentId, Guid versionId, string recipientUserId, string sharedByUserId, string sharedByName, DateTime sharedUtc, string? note = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SharedDocumentShare(DocumentId,VersionId,RecipientUserId,SharedByUserId,SharedByName,SharedUtc,Note,IsRevoked) VALUES($document,$version,$recipient,$by,$name,$utc,$note,0) ON CONFLICT(DocumentId,VersionId,RecipientUserId) DO UPDATE SET SharedByUserId=$by,SharedByName=$name,SharedUtc=$utc,Note=$note,IsRevoked=0";
        command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", versionId.ToString()); command.Parameters.AddWithValue("$recipient", recipientUserId); command.Parameters.AddWithValue("$by", sharedByUserId); command.Parameters.AddWithValue("$name", sharedByName); command.Parameters.AddWithValue("$utc", sharedUtc.ToUniversalTime().ToString("O")); command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevokeShareAsync(Guid documentId, Guid versionId, string recipientUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE SharedDocumentShare SET IsRevoked=1 WHERE DocumentId=$document AND VersionId=$version AND RecipientUserId=$recipient"; command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", versionId.ToString()); command.Parameters.AddWithValue("$recipient", recipientUserId); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuerySql(string extra, bool includeRevoked = false) => $"SELECT d.DocumentId,v.VersionId,d.OriginalFileName,v.VersionNumber,ws.Status,rd.DecidedUtc,COALESCE(rd.ApproverDisplayName,s.SharedByName),COALESCE(rd.ApproverUserId,s.SharedByUserId),s.SharedByName,s.SharedByUserId,s.SharedUtc,s.Note,d.DocumentType,v.StoredRelativePath,rs.ApproverSignatureJson,rs.ApproverSignatureFinalized,rs.InternalApprovalStampJson,rs.InternalApprovalStampFinalized,s.IsRevoked FROM SharedDocumentShare s JOIN Document d ON d.DocumentId=s.DocumentId JOIN DocumentVersion v ON v.VersionId=s.VersionId JOIN WorkflowState ws ON ws.PackageId=d.DocumentId LEFT JOIN ReviewDecision rd ON rd.PackageId=d.DocumentId AND rd.Revision=v.VersionNumber AND rd.IsFinalApproval=1 LEFT JOIN ReviewSubmission rs ON rs.PackageId=d.DocumentId AND rs.Revision=v.VersionNumber WHERE s.RecipientUserId=$recipient AND {(includeRevoked ? "1=1" : "s.IsRevoked=0")} AND d.Status=1 AND ws.Status IN (11,12) {extra} ORDER BY s.SharedUtc DESC";

    private static async Task<IReadOnlyList<SharedDocument>> ReadAsync(SqliteCommand command, CancellationToken token)
    {
        var result = new List<SharedDocument>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var documentId = Guid.Parse(reader.GetString(0));
            var revision = reader.GetInt32(3);
            if (reader.GetInt32(18) != 0)
            {
                result.Add(new SharedDocument { DocumentId = documentId, VersionId = Guid.Parse(reader.GetString(1)), IsRevoked = true });
                continue;
            }
            SignatureData? signature = null;
            InternalApprovalStamp? stamp = null;
            string? approvalError = null;
            try
            {
                if (!reader.IsDBNull(14))
                {
                    signature = JsonSerializer.Deserialize<SignatureData>(reader.GetString(14)) ?? throw new InvalidDataException("Stored approver signature data is empty.");
                    if (reader.IsDBNull(15) || reader.GetInt32(15) != 1 || !signature.IsApprover || signature.DocumentId != documentId || signature.Revision != revision) throw new InvalidDataException("Stored approver signature identity or finalization is invalid.");
                    signature.Validate();
                }
                if (!reader.IsDBNull(16))
                {
                    stamp = JsonSerializer.Deserialize<InternalApprovalStamp>(reader.GetString(16)) ?? throw new InvalidDataException("Stored internal approval stamp data is empty.");
                    if (reader.IsDBNull(17) || reader.GetInt32(17) != 1 || stamp.DocumentId != documentId || stamp.PackageId != documentId || stamp.Revision != revision) throw new InvalidDataException("Stored internal approval stamp identity or finalization is invalid.");
                    stamp.Validate();
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            {
                approvalError = exception.Message;
            }
            result.Add(new SharedDocument { DocumentId = documentId, VersionId = Guid.Parse(reader.GetString(1)), DocumentName = reader.GetString(2), Revision = revision, ApprovalStatus = (WorkflowStatus)reader.GetInt32(4), FinalApprovalUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind), ApproverName = reader.GetString(6), ApproverUserId = reader.GetString(7), SharedByName = reader.GetString(8), SharedByUserId = reader.GetString(9), SharedUtc = DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind), SharingNote = reader.IsDBNull(11) ? null : reader.GetString(11), DocumentType = (DrawVerify.Core.Enums.DocumentType)reader.GetInt32(12), StoredRelativePath = reader.GetString(13), ApproverSignature = signature, ApprovalStamp = stamp, ApprovalInformationError = approvalError });
        }
        return result;
    }
}
