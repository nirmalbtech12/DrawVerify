using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for audit entries, implementing IAuditLogger.
/// </summary>
public sealed class SqliteAuditRepository : IAuditLogger
{
    private readonly string _connectionString;
    public SqliteAuditRepository(string databasePath) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); Initialize(); }
    private void Initialize() { using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL)";cmd.ExecuteNonQuery(); }
    public async Task WriteAsync(AuditEntry entry, CancellationToken token = default) { await using var c=new SqliteConnection(_connectionString);await c.OpenAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO DocumentAuditLog VALUES ($id,$document,$action,$user,$time,$details)";cmd.Parameters.AddWithValue("$id",entry.AuditId.ToString());cmd.Parameters.AddWithValue("$document",(object?)entry.DocumentId?.ToString()??DBNull.Value);cmd.Parameters.AddWithValue("$action",entry.Action);cmd.Parameters.AddWithValue("$user",entry.UserId);cmd.Parameters.AddWithValue("$time",entry.TimestampUtc.ToString("O"));cmd.Parameters.AddWithValue("$details",entry.DetailsJson);await cmd.ExecuteNonQueryAsync(token); }
}
