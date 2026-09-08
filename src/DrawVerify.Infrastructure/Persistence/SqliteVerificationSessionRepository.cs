using System.Globalization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteVerificationSessionRepository : IVerificationSessionRepository
{
    private readonly string _connectionString;
    public SqliteVerificationSessionRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS VerificationSession (VerificationSessionId TEXT PRIMARY KEY, StepDocumentId TEXT NOT NULL, DxfDocumentId TEXT NOT NULL, StepDocumentVersionId TEXT NULL, DxfDocumentVersionId TEXT NULL, Status INTEGER NOT NULL, ErrorMessage TEXT NULL, UpdatedUtc TEXT NOT NULL, UNIQUE(StepDocumentId,DxfDocumentId)); CREATE TABLE IF NOT EXISTS DimensionMatch (DimensionMatchId TEXT PRIMARY KEY, VerificationSessionId TEXT NOT NULL, DxfDocumentVersionId TEXT NOT NULL, DxfDimensionReference INTEGER NOT NULL, StepDocumentVersionId TEXT NOT NULL, StepGeometryReference TEXT NULL, GeometryType TEXT NULL, ConfidenceScore REAL NULL, Confidence INTEGER NULL, RequiresManualReview INTEGER NOT NULL, Status INTEGER NOT NULL, ReasonCode TEXT NULL, FOREIGN KEY(VerificationSessionId) REFERENCES VerificationSession(VerificationSessionId)); CREATE INDEX IF NOT EXISTS IX_DimensionMatch_SessionStatus ON DimensionMatch(VerificationSessionId,Status);";
        command.ExecuteNonQuery();
    }
    public async Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default)
    {
        if (stepDocumentId == Guid.Empty || dxfDocumentId == Guid.Empty) throw new ArgumentException("Document IDs are required.");
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO VerificationSession (VerificationSessionId,StepDocumentId,DxfDocumentId,Status,UpdatedUtc) VALUES ($id,$step,$dxf,0,$updated) ON CONFLICT(StepDocumentId,DxfDocumentId) DO NOTHING; SELECT VerificationSessionId,StepDocumentId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,Status,ErrorMessage,UpdatedUtc FROM VerificationSession WHERE StepDocumentId=$step AND DxfDocumentId=$dxf;";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$step", stepDocumentId.ToString()); command.Parameters.AddWithValue("$dxf", dxfDocumentId.ToString()); command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); return MapSession(reader);
    }
    public async Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT VerificationSessionId,StepDocumentId,DxfDocumentId,StepDocumentVersionId,DxfDocumentVersionId,Status,ErrorMessage,UpdatedUtc FROM VerificationSession WHERE VerificationSessionId=$id"; command.Parameters.AddWithValue("$id", sessionId.ToString()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }
    public async Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT DimensionMatchId,VerificationSessionId,DxfDocumentVersionId,DxfDimensionReference,StepDocumentVersionId,StepGeometryReference,GeometryType,ConfidenceScore,Confidence,RequiresManualReview,Status,ReasonCode FROM DimensionMatch WHERE VerificationSessionId=$id" + (status is null ? string.Empty : " AND Status=$status") + " ORDER BY DxfDimensionReference"; command.Parameters.AddWithValue("$id", sessionId.ToString()); if (status is not null) command.Parameters.AddWithValue("$status", status.Value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<DimensionMatch>(); while (await reader.ReadAsync(cancellationToken)) result.Add(MapMatch(reader)); return result;
    }
    public async Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(matches); if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Audit user is required.", nameof(userId));
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await Exec(connection, transaction, "DELETE FROM DimensionMatch WHERE VerificationSessionId=$id", cancellationToken, ("$id", session.VerificationSessionId.ToString()));
            foreach (var match in matches)
            {
                if (match.VerificationSessionId != session.VerificationSessionId || match.Status == 0 || match.DxfDocumentVersionId == Guid.Empty || match.StepDocumentVersionId == Guid.Empty) throw new InvalidOperationException("Invalid dimension match reference.");
                await Exec(connection, transaction, "INSERT INTO DimensionMatch VALUES ($match,$session,$dxf,$ordinal,$step,$reference,$type,$score,$confidence,$review,$status,$reason)", cancellationToken, ("$match", match.DimensionMatchId.ToString()), ("$session", match.VerificationSessionId.ToString()), ("$dxf", match.DxfDocumentVersionId.ToString()), ("$ordinal", match.DxfDimensionReference), ("$step", match.StepDocumentVersionId.ToString()), ("$reference", (object?)match.StepGeometryReference ?? DBNull.Value), ("$type", (object?)match.GeometryType ?? DBNull.Value), ("$score", (object?)match.ConfidenceScore ?? DBNull.Value), ("$confidence", (object?)match.Confidence ?? DBNull.Value), ("$review", match.RequiresManualReview ? 1 : 0), ("$status", match.Status), ("$reason", (object?)match.ReasonCode ?? DBNull.Value));
            }
            await Exec(connection, transaction, "UPDATE VerificationSession SET StepDocumentVersionId=$step,DxfDocumentVersionId=$dxf,Status=$status,ErrorMessage=$error,UpdatedUtc=$updated WHERE VerificationSessionId=$id", cancellationToken, ("$step", (object?)session.StepDocumentVersionId?.ToString() ?? DBNull.Value), ("$dxf", (object?)session.DxfDocumentVersionId?.ToString() ?? DBNull.Value), ("$status", session.Status), ("$error", (object?)session.ErrorMessage ?? DBNull.Value), ("$updated", session.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$id", session.VerificationSessionId.ToString()));
            await Exec(connection, transaction, "INSERT INTO DocumentAuditLog (AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson) VALUES ($id,NULL,$action,$user,$time,$details)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$action", auditAction), ("$user", userId), ("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$details", detailsJson));
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }
    private static async Task Exec(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object Value)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); await command.ExecuteNonQueryAsync(token); }
    private static VerificationSession MapSession(SqliteDataReader r) => new() { VerificationSessionId = Guid.Parse(r.GetString(0)), StepDocumentId = Guid.Parse(r.GetString(1)), DxfDocumentId = Guid.Parse(r.GetString(2)), StepDocumentVersionId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), DxfDocumentVersionId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)), Status = (VerificationSessionStatus)r.GetInt32(5), ErrorMessage = r.IsDBNull(6) ? null : r.GetString(6), UpdatedUtc = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
    private static DimensionMatch MapMatch(SqliteDataReader r) => new() { DimensionMatchId = Guid.Parse(r.GetString(0)), VerificationSessionId = Guid.Parse(r.GetString(1)), DxfDocumentVersionId = Guid.Parse(r.GetString(2)), DxfDimensionReference = r.GetInt32(3), StepDocumentVersionId = Guid.Parse(r.GetString(4)), StepGeometryReference = r.IsDBNull(5) ? null : r.GetString(5), GeometryType = r.IsDBNull(6) ? null : r.GetString(6), ConfidenceScore = r.IsDBNull(7) ? null : r.GetDouble(7), Confidence = r.IsDBNull(8) ? null : (MatchConfidence)r.GetInt32(8), RequiresManualReview = r.GetInt32(9) != 0, Status = (DimensionMatchStatus)r.GetInt32(10), ReasonCode = r.IsDBNull(11) ? null : r.GetString(11) };
}
