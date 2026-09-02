using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
<<<<<<< HEAD
=======

>>>>>>> 642060c (KAN-19: implementation)
namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for audit entries, implementing IAuditLogger.
/// </summary>
public sealed class SqliteAuditRepository : IAuditLogger
{
<<<<<<< HEAD
    private readonly SqliteConnection _connection;
    public SqliteAuditRepository(string databasePath = ":memory:")
    {
        _connection = new SqliteConnection(databasePath == ":memory:" ? "Data Source=:memory:;Cache=Shared" : $"Data Source={databasePath}");
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS DocumentAuditLog (Id TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, Action TEXT NOT NULL, UserId TEXT NULL, Timestamp TEXT NOT NULL, DetailsJson TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
    public IReadOnlyList<AuditEntry> Entries { get { var result=new List<AuditEntry>(); using var q=_connection.CreateCommand(); q.CommandText="SELECT Id,DocumentId,Action,UserId,Timestamp,DetailsJson FROM DocumentAuditLog ORDER BY Timestamp,Id"; using var r=q.ExecuteReader(); while(r.Read()) result.Add(new AuditEntry { Id=Guid.Parse(r.GetString(0)), DocumentId=Guid.Parse(r.GetString(1)), Action=r.GetString(2), UserId=r.IsDBNull(3)?null:Guid.Parse(r.GetString(3)), Timestamp=DateTimeOffset.Parse(r.GetString(4)), DetailsJson=r.GetString(5) }); return result; } }
    public void Append(AuditEntry entry) { using var q=_connection.CreateCommand(); q.CommandText="INSERT INTO DocumentAuditLog(Id,DocumentId,Action,UserId,Timestamp,DetailsJson) VALUES($id,$document,$action,$user,$timestamp,$details)"; q.Parameters.AddWithValue("$id",entry.Id.ToString()); q.Parameters.AddWithValue("$document",entry.DocumentId.ToString()); q.Parameters.AddWithValue("$action",entry.Action); q.Parameters.AddWithValue("$user",(object?)entry.UserId?.ToString()??DBNull.Value); q.Parameters.AddWithValue("$timestamp",entry.Timestamp.ToString("O")); q.Parameters.AddWithValue("$details",entry.DetailsJson); q.ExecuteNonQuery(); }
    public void Dispose() => _connection.Dispose();
=======
    private readonly string _connectionString;
    public SqliteAuditRepository(string databasePath) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); Initialize(); }
    private void Initialize() { using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS DocumentAuditLog (AuditId TEXT PRIMARY KEY, DocumentId TEXT NULL, Action INTEGER NOT NULL, UserId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, DetailsJson TEXT NOT NULL)";cmd.ExecuteNonQuery(); }
    public async Task WriteAsync(AuditEntry entry, CancellationToken token = default) { await using var c=new SqliteConnection(_connectionString);await c.OpenAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO DocumentAuditLog VALUES ($id,$document,$action,$user,$time,$details)";cmd.Parameters.AddWithValue("$id",entry.AuditId.ToString());cmd.Parameters.AddWithValue("$document",(object?)entry.DocumentId?.ToString()??DBNull.Value);cmd.Parameters.AddWithValue("$action",entry.Action);cmd.Parameters.AddWithValue("$user",entry.UserId);cmd.Parameters.AddWithValue("$time",entry.TimestampUtc.ToString("O"));cmd.Parameters.AddWithValue("$details",entry.DetailsJson);await cmd.ExecuteNonQueryAsync(token); }
>>>>>>> 642060c (KAN-19: implementation)
}
