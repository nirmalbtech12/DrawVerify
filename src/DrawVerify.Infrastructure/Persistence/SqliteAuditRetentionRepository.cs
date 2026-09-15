using DrawVerify.Core.Interfaces;
using Microsoft.Data.Sqlite;
namespace DrawVerify.Infrastructure.Persistence;
public sealed class SqliteAuditRetentionRepository : IAuditRetentionRepository
{
    private readonly string _connectionString; private readonly ICentralizedLogger? _logger;
    public SqliteAuditRetentionRepository(string databasePath, ICentralizedLogger? logger = null) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); _logger = logger; }
    public async Task<int> ApplyAsync(int retentionDays, CancellationToken cancellationToken = default) { if (retentionDays < 0) throw new ArgumentOutOfRangeException(nameof(retentionDays)); if (retentionDays == 0) return 0; try { await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var q = c.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocumentAuditLog'"; var hasDocumentAudit = Convert.ToInt32(await q.ExecuteScalarAsync(cancellationToken)) > 0; q.CommandText = hasDocumentAudit ? "DELETE FROM DocumentAuditLog WHERE TimestampUtc < $cutoff" : "DELETE FROM AuthenticationAuditLog WHERE TimestampUtc < $cutoff"; q.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O")); return await q.ExecuteNonQueryAsync(cancellationToken); } catch (Exception ex) { _logger?.Log(ex, "SQLite audit retention", $"Unable to apply {retentionDays}-day audit retention."); throw; } }
}
