using System.Text.Json;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
namespace DrawVerify.Infrastructure.Persistence;
public sealed class SqliteWorkflowConfigurationRepository : IWorkflowConfigurationRepository
{
    private readonly string _database;
    private readonly ICentralizedLogger? _logger;
    private readonly JsonSerializerOptions _json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public SqliteWorkflowConfigurationRepository(string database, ICentralizedLogger? logger = null) { _database = database; _logger = logger; }
    private SqliteConnection Open()
    {
        try
        {
            var c = new SqliteConnection($"Data Source={_database}"); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS WorkflowConfigurationVersions (Version INTEGER PRIMARY KEY, Payload TEXT NOT NULL, IsCurrent INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS WorkflowConfigurationAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, ActorId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, PreviousVersion INTEGER NULL, NewVersion INTEGER NOT NULL, PreviousValue TEXT NOT NULL, NewValue TEXT NOT NULL); CREATE TABLE IF NOT EXISTS WorkflowInstances (WorkflowId TEXT PRIMARY KEY, ConfigurationVersion INTEGER NULL, StartedUtc TEXT NULL);";
            cmd.ExecuteNonQuery();
            EnsureWorkflowInstancesColumns(c);
            EnsureColumn(c, "WorkflowState", "ConfigurationVersion", "INTEGER NULL"); EnsureColumn(c, "ReviewSubmission", "ConfigurationVersion", "INTEGER NULL");
            return c;
        }
        catch (Exception exception) { _logger?.Log(exception, "Workflow configuration database migration", $"Unable to initialize SQLite schema for '{_database}'."); throw; }
    }
    private static void EnsureWorkflowInstancesColumns(SqliteConnection connection)
    {
        var hasLegacyId = HasColumn(connection, "WorkflowInstances", "Id");
        EnsureColumn(connection, "WorkflowInstances", "WorkflowId", "TEXT NULL");
        EnsureColumn(connection, "WorkflowInstances", "StartedUtc", "TEXT NULL");
        if (hasLegacyId) { using var update = connection.CreateCommand(); update.CommandText = "UPDATE WorkflowInstances SET WorkflowId=Id WHERE WorkflowId IS NULL AND Id IS NOT NULL"; update.ExecuteNonQuery(); }
    }
    private static bool HasColumn(SqliteConnection connection, string table, string column)
    { using var info=connection.CreateCommand(); info.CommandText=$"PRAGMA table_info({table})"; using var reader=info.ExecuteReader(); while(reader.Read()) if(string.Equals(reader.GetString(1),column,StringComparison.OrdinalIgnoreCase)) return true; return false; }
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    { using var exists=connection.CreateCommand(); exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table"; exists.Parameters.AddWithValue("$table",table); if(Convert.ToInt32(exists.ExecuteScalar())==0)return; var found=false; using(var info=connection.CreateCommand()) { info.CommandText=$"PRAGMA table_info({table})"; using var reader=info.ExecuteReader(); while(reader.Read()) if(string.Equals(reader.GetString(1),column,StringComparison.OrdinalIgnoreCase)) { found=true; break; } } if(found)return; using var alter=connection.CreateCommand(); alter.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} {definition}"; alter.ExecuteNonQuery(); }
    public async Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default) => await GetAsync(null, cancellationToken);
    public async Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default) => await GetAsync(version, cancellationToken);
    private async Task<WorkflowConfiguration?> GetAsync(int? version, CancellationToken token)
    { try { using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText=version.HasValue ? "SELECT Payload FROM WorkflowConfigurationVersions WHERE Version=$version LIMIT 1" : "SELECT Payload FROM WorkflowConfigurationVersions WHERE IsCurrent=1 LIMIT 1"; if (version.HasValue) cmd.Parameters.AddWithValue("$version", version.Value); var value=await cmd.ExecuteScalarAsync(token); return value is string payload ? JsonSerializer.Deserialize<WorkflowConfiguration>(payload,_json) : null; } catch (Exception exception) { _logger?.Log(exception, "Workflow configuration read", $"Unable to read version={(version?.ToString() ?? "current")} from '{_database}'."); throw; } }
    public async Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken token = default)
    { try { using var c=Open(); using var tx=c.BeginTransaction(); using var check=c.CreateCommand(); check.Transaction=tx; check.CommandText="SELECT Version FROM WorkflowConfigurationVersions WHERE IsCurrent=1"; var current=await check.ExecuteScalarAsync(token); if (expectedVersion.HasValue && current is not null && Convert.ToInt32(current)!=expectedVersion.Value) throw new InvalidOperationException($"Workflow configuration version conflict. Expected {expectedVersion}, current {current}."); using var insert=c.CreateCommand(); insert.Transaction=tx; insert.CommandText="UPDATE WorkflowConfigurationVersions SET IsCurrent=0; INSERT INTO WorkflowConfigurationVersions(Version,Payload,IsCurrent) VALUES($v,$p,1); INSERT INTO WorkflowConfigurationAudit(ActorId,TimestampUtc,PreviousVersion,NewVersion,PreviousValue,NewValue) VALUES($a,$t,$pv,$nv,$old,$new)"; insert.Parameters.AddWithValue("$v",configuration.Version); insert.Parameters.AddWithValue("$p",JsonSerializer.Serialize(configuration,_json)); insert.Parameters.AddWithValue("$a",audit.ActorId); insert.Parameters.AddWithValue("$t",audit.TimestampUtc.ToString("O")); insert.Parameters.AddWithValue("$pv",(object?)audit.PreviousVersion ?? DBNull.Value); insert.Parameters.AddWithValue("$nv",audit.NewVersion); insert.Parameters.AddWithValue("$old",audit.PreviousValue); insert.Parameters.AddWithValue("$new",audit.NewValue); await insert.ExecuteNonQueryAsync(token); tx.Commit(); return configuration; } catch (Exception exception) { _logger?.Log(exception, "Workflow configuration save", $"Unable to persist version={configuration.Version} in '{_database}'."); throw; } }
    public async Task CaptureWorkflowVersionAsync(string workflowId, int? version, CancellationToken token = default)
    { try { using var c=Open(); using var tx=c.BeginTransaction(); using var update=c.CreateCommand(); update.Transaction=tx; update.CommandText="UPDATE WorkflowInstances SET ConfigurationVersion=$version WHERE WorkflowId=$id"; update.Parameters.AddWithValue("$id",workflowId); update.Parameters.AddWithValue("$version",(object?)version??DBNull.Value); if(await update.ExecuteNonQueryAsync(token)==0) { using var insert=c.CreateCommand(); insert.Transaction=tx; insert.CommandText="INSERT INTO WorkflowInstances(WorkflowId,ConfigurationVersion,StartedUtc) VALUES($id,$version,$started)"; insert.Parameters.AddWithValue("$id",workflowId); insert.Parameters.AddWithValue("$version",(object?)version??DBNull.Value); insert.Parameters.AddWithValue("$started",DateTime.UtcNow.ToString("O")); await insert.ExecuteNonQueryAsync(token); } tx.Commit(); } catch (Exception exception) { _logger?.Log(exception, "Workflow version capture", $"Unable to capture configuration version for workflow='{workflowId}' in '{_database}'."); throw; } }
    public async Task<int?> GetWorkflowVersionAsync(string workflowId, CancellationToken token = default)
    { try { using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT ConfigurationVersion FROM WorkflowInstances WHERE WorkflowId=$id"; cmd.Parameters.AddWithValue("$id",workflowId); var result=await cmd.ExecuteScalarAsync(token); return result is null or DBNull ? null : Convert.ToInt32(result); } catch (Exception exception) { _logger?.Log(exception, "Workflow version read", $"Unable to read configuration version for workflow='{workflowId}' in '{_database}'."); throw; } }
}
