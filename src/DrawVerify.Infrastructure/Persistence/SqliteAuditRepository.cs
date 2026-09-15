using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using DrawVerify.Core.Enums;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for audit entries, implementing IAuditLogger.
/// </summary>
public sealed class SqliteAuditRepository : IAuditLogger, IDocumentAuditRepository
{
    private readonly string _connectionString;
    public SqliteAuditRepository(string databasePath) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); Initialize(); }
    private void Initialize() { using var c = new SqliteConnection(_connectionString); c.Open(); EnsureSchema(c); }
    public static void EnsureSchema(SqliteConnection c)
    {
        using var command = c.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL, ActorRole TEXT NULL, DocumentRevision TEXT NULL, DocumentVersion TEXT NULL, EventMetadataJson TEXT NOT NULL DEFAULT '{}', PreviousWorkflowStatus TEXT NULL, NewWorkflowStatus TEXT NULL, PredecessorAuditId TEXT NULL, IntegrityHash TEXT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA table_info(DocumentAuditLog)";
        var hasUserId = false;
        using (var reader = command.ExecuteReader()) while (reader.Read()) hasUserId |= string.Equals(reader.GetString(1), "UserId", StringComparison.OrdinalIgnoreCase);
        if (!hasUserId)
        {
            command.CommandText = "ALTER TABLE DocumentAuditLog ADD COLUMN UserId TEXT NULL";
            command.ExecuteNonQuery();
        }
        foreach (var (name, definition) in new[] { ("ActorRole", "TEXT NULL"), ("DocumentRevision", "TEXT NULL"), ("DocumentVersion", "TEXT NULL"), ("EventMetadataJson", "TEXT NOT NULL DEFAULT '{}'"), ("PreviousWorkflowStatus", "TEXT NULL"), ("NewWorkflowStatus", "TEXT NULL"), ("PredecessorAuditId", "TEXT NULL"), ("IntegrityHash", "TEXT NULL") })
        {
            command.CommandText = $"ALTER TABLE DocumentAuditLog ADD COLUMN {name} {definition}";
            try { command.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        }
        command.CommandText = "CREATE INDEX IF NOT EXISTS IX_DocumentAuditLog_Query ON DocumentAuditLog(DocumentId,TimestampUtc,Action,UserId,DocumentRevision)"; command.ExecuteNonQuery();
        command.CommandText = "CREATE TRIGGER IF NOT EXISTS TR_DocumentAuditLog_PreventDelete BEFORE DELETE ON DocumentAuditLog BEGIN SELECT RAISE(ABORT, 'Document audit entries are append-only.'); END;"; command.ExecuteNonQuery();
        command.CommandText = "CREATE TRIGGER IF NOT EXISTS TR_DocumentAuditLog_PreventUpdate BEFORE UPDATE ON DocumentAuditLog BEGIN SELECT RAISE(ABORT, 'Document audit entries are append-only.'); END;"; command.ExecuteNonQuery();
    }
    public async Task WriteAsync(AuditEntry entry, CancellationToken token = default)
    {
        if (entry.DocumentId.HasValue) entry.Validate();
        else
        {
            if (string.IsNullOrWhiteSpace(entry.UserId)) throw new ArgumentException("Audit actor is required.", nameof(entry.UserId));
            if (!Enum.IsDefined(entry.Action)) throw new ArgumentException("A supported audit action is required.", nameof(entry.Action));
            if (entry.TimestampUtc == default && entry.TimestampOffset == default) throw new ArgumentException("Audit timestamp is required.", nameof(entry.TimestampUtc));
        }
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token);
        await using var cmd = c.CreateCommand();
        var timestamp = entry.TimestampOffset == default ? new DateTimeOffset(DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc)) : entry.TimestampOffset;
        var hash = entry.IntegrityHash ?? (entry.DocumentId.HasValue ? await ComputeHashAsync(c, entry, token) : null);
        cmd.CommandText = "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,ActorRole,DocumentRevision,DocumentVersion,EventMetadataJson,PreviousWorkflowStatus,NewWorkflowStatus,PredecessorAuditId,IntegrityHash) VALUES ($id,$document,$action,$user,$time,$details,$role,$revision,$version,$metadata,$previous,$next,$predecessor,$hash)";
        cmd.Parameters.AddWithValue("$id", entry.AuditId.ToString()); cmd.Parameters.AddWithValue("$document", (object?)entry.DocumentId?.ToString() ?? DBNull.Value); cmd.Parameters.AddWithValue("$action", entry.Action); cmd.Parameters.AddWithValue("$user", entry.UserId); cmd.Parameters.AddWithValue("$time", timestamp.ToString("O")); cmd.Parameters.AddWithValue("$details", entry.DetailsJson); cmd.Parameters.AddWithValue("$role", (object?)entry.ActorRole ?? DBNull.Value); cmd.Parameters.AddWithValue("$revision", (object?)entry.DocumentRevision ?? DBNull.Value); cmd.Parameters.AddWithValue("$version", (object?)entry.DocumentVersion ?? DBNull.Value); cmd.Parameters.AddWithValue("$metadata", entry.EventMetadataJson); cmd.Parameters.AddWithValue("$previous", (object?)entry.PreviousWorkflowStatus ?? DBNull.Value); cmd.Parameters.AddWithValue("$next", (object?)entry.NewWorkflowStatus ?? DBNull.Value); cmd.Parameters.AddWithValue("$predecessor", (object?)entry.PredecessorAuditId?.ToString() ?? DBNull.Value); cmd.Parameters.AddWithValue("$hash", (object?)hash ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(token);
    }

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) { entry.Validate(); if (!entry.DocumentId.HasValue || entry.DocumentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(entry.DocumentId)); return WriteAsync(entry, cancellationToken); }
    public async Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken token = default)
    {
        query.Validate();
        if (!query.DocumentId.HasValue) throw new ArgumentException("Document identity is required for document audit queries.", nameof(query));
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AuditId,DocumentId,DocumentRevision,DocumentVersion,UserId,ActorRole,TimestampUtc,Action,DetailsJson,EventMetadataJson,PreviousWorkflowStatus,NewWorkflowStatus,PredecessorAuditId,IntegrityHash FROM DocumentAuditLog WHERE DocumentId=$document AND ($from IS NULL OR TimestampUtc >= $from) AND ($to IS NULL OR TimestampUtc <= $to) AND ($action IS NULL OR Action=$action) AND ($actor IS NULL OR UserId=$actor) AND ($status IS NULL OR PreviousWorkflowStatus=$status OR NewWorkflowStatus=$status) AND ($revision IS NULL OR DocumentRevision=$revision OR DocumentVersion=$revision) AND ($search IS NULL OR DetailsJson LIKE $search OR EventMetadataJson LIKE $search) ORDER BY TimestampUtc,AuditId LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$document", query.DocumentId.Value.ToString()); cmd.Parameters.AddWithValue("$from", query.FromUtc?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("$to", query.ToUtc?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("$action", (object?)ParseAction(query.Action) ?? DBNull.Value); cmd.Parameters.AddWithValue("$actor", (object?)query.ActorUsername ?? (object?)query.Username ?? DBNull.Value); cmd.Parameters.AddWithValue("$status", (object?)query.WorkflowStatus ?? DBNull.Value); cmd.Parameters.AddWithValue("$revision", (object?)query.Revision ?? DBNull.Value); cmd.Parameters.AddWithValue("$search", string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : $"%{query.Search}%"); cmd.Parameters.AddWithValue("$limit", query.PageSize); cmd.Parameters.AddWithValue("$offset", (query.Page - 1) * query.PageSize);
        var results = new List<DocumentAuditRecord>(); await using var reader = await cmd.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) results.Add(new DocumentAuditRecord { AuditId=Guid.Parse(reader.GetString(0)), DocumentId=Guid.Parse(reader.GetString(1)), DocumentRevision=Text(reader,2), DocumentVersion=Text(reader,3), ActorId=reader.GetString(4), ActorRole=Text(reader,5), Timestamp=DateTimeOffset.Parse(reader.GetString(6)), Action=(DocumentAuditAction)reader.GetInt32(7), DetailsJson=reader.GetString(8), EventMetadataJson=reader.GetString(9), PreviousWorkflowStatus=Text(reader,10), NewWorkflowStatus=Text(reader,11), PredecessorAuditId=reader.IsDBNull(12)?null:Guid.Parse(reader.GetString(12)), IntegrityHash=Text(reader,13) }); return results;
    }
    public async Task VerifyIntegrityAsync(Guid documentId, CancellationToken token = default)
    {
        var records = await QueryAsync(new AuditLogQuery(DocumentId: documentId, PageSize: 1000), token); string? previousHash = null; Guid? previousId = null; foreach (var record in records) { if (record.IntegrityHash is null) throw new InvalidOperationException($"Audit integrity data is missing for document {documentId}."); if (record.PredecessorAuditId.HasValue && record.PredecessorAuditId != previousId) throw new InvalidOperationException($"Audit integrity predecessor verification failed for document {documentId}."); var expected = ComputeHash(previousHash ?? string.Empty, record); if (!string.Equals(expected, record.IntegrityHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Audit integrity hash verification failed for document {documentId}."); previousHash = record.IntegrityHash; previousId = record.AuditId; }
    }
    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static int? ParseAction(string? value) => string.IsNullOrWhiteSpace(value) ? null : Enum.TryParse<DocumentAuditAction>(value, true, out var action) ? (int)action : throw new ArgumentException("Unsupported audit action.", nameof(value));
    private static async Task<string> ComputeHashAsync(SqliteConnection connection, AuditEntry entry, CancellationToken token) { if (!entry.DocumentId.HasValue) throw new ArgumentException("Document identity is required for integrity hashing.", nameof(entry)); await using var q=connection.CreateCommand(); q.CommandText="SELECT IntegrityHash FROM DocumentAuditLog WHERE DocumentId=$document ORDER BY TimestampUtc DESC,AuditId DESC LIMIT 1"; q.Parameters.AddWithValue("$document",entry.DocumentId.Value.ToString()); var previous=await q.ExecuteScalarAsync(token) as string ?? string.Empty; var timestamp = entry.TimestampOffset == default ? new DateTimeOffset(DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc)) : entry.TimestampOffset; var payload=$"{previous}|{entry.AuditId}|{entry.DocumentId}|{entry.Action}|{entry.UserId}|{timestamp:O}|{entry.DocumentRevision}|{entry.EventMetadataJson}|{entry.DetailsJson}|{entry.ActorRole}|{entry.DocumentVersion}|{entry.PreviousWorkflowStatus}|{entry.NewWorkflowStatus}"; return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))); }
    private static string ComputeHash(string previous, DocumentAuditRecord record) { var payload=$"{previous}|{record.AuditId}|{record.DocumentId}|{record.Action}|{record.ActorId}|{record.Timestamp:O}|{record.DocumentRevision}|{record.EventMetadataJson}|{record.DetailsJson}|{record.ActorRole}|{record.DocumentVersion}|{record.PreviousWorkflowStatus}|{record.NewWorkflowStatus}"; return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))); }
}
