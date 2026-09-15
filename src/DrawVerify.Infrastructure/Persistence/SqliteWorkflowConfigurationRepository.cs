using System.Text.Json;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
namespace DrawVerify.Infrastructure.Persistence;
public sealed class SqliteWorkflowConfigurationRepository : IWorkflowConfigurationRepository
{
    private readonly string _database;
    private readonly JsonSerializerOptions _json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public SqliteWorkflowConfigurationRepository(string database) => _database = database;
    private SqliteConnection Open() { var c = new SqliteConnection($"Data Source={_database}"); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="CREATE TABLE IF NOT EXISTS WorkflowConfigurationVersions (Version INTEGER PRIMARY KEY, Payload TEXT NOT NULL, IsCurrent INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS WorkflowConfigurationAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, ActorId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, PreviousVersion INTEGER NULL, NewVersion INTEGER NOT NULL, PreviousValue TEXT NOT NULL, NewValue TEXT NOT NULL);"; cmd.ExecuteNonQuery(); return c; }
    public async Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default) => await GetAsync(null, cancellationToken);
    public async Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default) => await GetAsync(version, cancellationToken);
    private async Task<WorkflowConfiguration?> GetAsync(int? version, CancellationToken token)
    { using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText=version.HasValue ? "SELECT Payload FROM WorkflowConfigurationVersions WHERE Version=$version LIMIT 1" : "SELECT Payload FROM WorkflowConfigurationVersions WHERE IsCurrent=1 LIMIT 1"; if (version.HasValue) cmd.Parameters.AddWithValue("$version", version.Value); var value=await cmd.ExecuteScalarAsync(token); return value is string payload ? JsonSerializer.Deserialize<WorkflowConfiguration>(payload,_json) : null; }
    public async Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken token = default)
    { using var c=Open(); using var tx=c.BeginTransaction(); using var check=c.CreateCommand(); check.Transaction=tx; check.CommandText="SELECT Version FROM WorkflowConfigurationVersions WHERE IsCurrent=1"; var current=await check.ExecuteScalarAsync(token); if (expectedVersion.HasValue && current is not null && Convert.ToInt32(current)!=expectedVersion.Value) throw new InvalidOperationException($"Workflow configuration version conflict. Expected {expectedVersion}, current {current}."); using var insert=c.CreateCommand(); insert.Transaction=tx; insert.CommandText="UPDATE WorkflowConfigurationVersions SET IsCurrent=0; INSERT INTO WorkflowConfigurationVersions(Version,Payload,IsCurrent) VALUES($v,$p,1); INSERT INTO WorkflowConfigurationAudit(ActorId,TimestampUtc,PreviousVersion,NewVersion,PreviousValue,NewValue) VALUES($a,$t,$pv,$nv,$old,$new)"; insert.Parameters.AddWithValue("$v",configuration.Version); insert.Parameters.AddWithValue("$p",JsonSerializer.Serialize(configuration,_json)); insert.Parameters.AddWithValue("$a",audit.ActorId); insert.Parameters.AddWithValue("$t",audit.TimestampUtc.ToString("O")); insert.Parameters.AddWithValue("$pv",(object?)audit.PreviousVersion ?? DBNull.Value); insert.Parameters.AddWithValue("$nv",audit.NewVersion); insert.Parameters.AddWithValue("$old",audit.PreviousValue); insert.Parameters.AddWithValue("$new",audit.NewValue); await insert.ExecuteNonQueryAsync(token); tx.Commit(); return configuration; }
}
