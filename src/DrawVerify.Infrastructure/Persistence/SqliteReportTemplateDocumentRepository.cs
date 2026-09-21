using System.Globalization;
using System.Text.Json;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteReportTemplateDocumentRepository : IReportTemplateDocumentRepository
{
    private readonly string _connectionString;
    public SqliteReportTemplateDocumentRepository(string databasePath) { _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); Initialize(); }
    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ReportTemplateSource (SourceId TEXT PRIMARY KEY, FileName TEXT NOT NULL, ContentType TEXT NULL, PdfBytes BLOB NOT NULL); CREATE TABLE IF NOT EXISTS ReportTemplateDocument (TemplateId TEXT NOT NULL, Version INTEGER NOT NULL, SourceId TEXT NOT NULL, SourceFileName TEXT NOT NULL, Content BLOB NOT NULL, CreatedUtc TEXT NOT NULL, PRIMARY KEY(TemplateId, Version)); CREATE TABLE IF NOT EXISTS TemplateConversionAudit (AuditId TEXT PRIMARY KEY, SourceId TEXT NOT NULL, SourceFileName TEXT NOT NULL, TemplateId TEXT NULL, Version INTEGER NULL, TimestampUtc TEXT NOT NULL, ActorId TEXT NOT NULL, Status TEXT NOT NULL, ErrorDetails TEXT NULL);";
        command.ExecuteNonQuery();
    }
    public async Task SaveSourceAsync(ReportTemplateSource source, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT OR REPLACE INTO ReportTemplateSource VALUES($id,$name,$type,$bytes)"; cmd.Parameters.AddWithValue("$id", source.SourceId.ToString()); cmd.Parameters.AddWithValue("$name", source.FileName); cmd.Parameters.AddWithValue("$type", (object?)source.ContentType ?? DBNull.Value); cmd.Parameters.Add("$bytes", SqliteType.Blob).Value = source.PdfBytes; await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<ReportTemplateSource?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT FileName,ContentType,PdfBytes FROM ReportTemplateSource WHERE SourceId=$id"; cmd.Parameters.AddWithValue("$id", sourceId.ToString()); await using var r = await cmd.ExecuteReaderAsync(cancellationToken); if (!await r.ReadAsync(cancellationToken)) return null; return new ReportTemplateSource { SourceId = sourceId, FileName = r.GetString(0), ContentType = r.IsDBNull(1) ? null : r.GetString(1), PdfBytes = (byte[])r[2] };
    }
    public async Task<ReportTemplateDocument> SaveAsync(ReportTemplateDocument document, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx = await c.BeginTransactionAsync(cancellationToken); if (expectedVersion is not null) { await using var check = c.CreateCommand(); check.Transaction = (SqliteTransaction)tx; check.CommandText = "SELECT COUNT(*) FROM ReportTemplateDocument WHERE TemplateId=$id AND Version=$version"; check.Parameters.AddWithValue("$id", document.TemplateId.ToString()); check.Parameters.AddWithValue("$version", expectedVersion); if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("Template document version conflict."); }
        await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx; cmd.CommandText = "INSERT INTO ReportTemplateDocument VALUES($id,$version,$source,$name,$content,$created)"; cmd.Parameters.AddWithValue("$id", document.TemplateId.ToString()); cmd.Parameters.AddWithValue("$version", document.Version); cmd.Parameters.AddWithValue("$source", document.SourceId.ToString()); cmd.Parameters.AddWithValue("$name", document.SourceFileName); cmd.Parameters.Add("$content", SqliteType.Blob).Value = document.Content; cmd.Parameters.AddWithValue("$created", document.CreatedUtc.ToString("O")); await cmd.ExecuteNonQueryAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return document;
    }
    public async Task<ReportTemplateDocument?> GetAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var cmd = c.CreateCommand(); cmd.CommandText = version is null ? "SELECT Version,SourceId,SourceFileName,Content,CreatedUtc FROM ReportTemplateDocument WHERE TemplateId=$id ORDER BY Version DESC LIMIT 1" : "SELECT Version,SourceId,SourceFileName,Content,CreatedUtc FROM ReportTemplateDocument WHERE TemplateId=$id AND Version=$version"; cmd.Parameters.AddWithValue("$id", templateId.ToString()); if (version is not null) cmd.Parameters.AddWithValue("$version", version); await using var r = await cmd.ExecuteReaderAsync(cancellationToken); if (!await r.ReadAsync(cancellationToken)) return null; return new ReportTemplateDocument { TemplateId = templateId, Version = r.GetInt32(0), SourceId = Guid.Parse(r.GetString(1)), SourceFileName = r.GetString(2), Content = (byte[])r[3], CreatedUtc = DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture) };
    }
    public async Task RecordConversionAuditAsync(TemplateConversionAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO TemplateConversionAudit VALUES($id,$source,$name,$template,$version,$timestamp,$actor,$status,$error)"; cmd.Parameters.AddWithValue("$id", entry.AuditId.ToString()); cmd.Parameters.AddWithValue("$source", entry.SourceId.ToString()); cmd.Parameters.AddWithValue("$name", entry.SourceFileName); cmd.Parameters.AddWithValue("$template", (object?)entry.TemplateId?.ToString() ?? DBNull.Value); cmd.Parameters.AddWithValue("$version", (object?)entry.Version ?? DBNull.Value); cmd.Parameters.AddWithValue("$timestamp", entry.TimestampUtc.ToString("O")); cmd.Parameters.AddWithValue("$actor", entry.ActorId); cmd.Parameters.AddWithValue("$status", entry.Status); cmd.Parameters.AddWithValue("$error", (object?)entry.ErrorDetails ?? DBNull.Value); await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<TemplateConversionAuditEntry>> GetConversionAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var cmd = c.CreateCommand(); cmd.CommandText = templateId is null ? "SELECT AuditId,SourceId,SourceFileName,TemplateId,Version,TimestampUtc,ActorId,Status,ErrorDetails FROM TemplateConversionAudit ORDER BY TimestampUtc" : "SELECT AuditId,SourceId,SourceFileName,TemplateId,Version,TimestampUtc,ActorId,Status,ErrorDetails FROM TemplateConversionAudit WHERE TemplateId=$template ORDER BY TimestampUtc"; if (templateId is not null) cmd.Parameters.AddWithValue("$template", templateId.Value.ToString()); var list = new List<TemplateConversionAuditEntry>(); await using var r = await cmd.ExecuteReaderAsync(cancellationToken); while (await r.ReadAsync(cancellationToken)) list.Add(new TemplateConversionAuditEntry { AuditId = Guid.Parse(r.GetString(0)), SourceId = Guid.Parse(r.GetString(1)), SourceFileName = r.GetString(2), TemplateId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), Version = r.IsDBNull(4) ? null : r.GetInt32(4), TimestampUtc = DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture), ActorId = r.GetString(6), Status = r.GetString(7), ErrorDetails = r.IsDBNull(8) ? null : r.GetString(8) }); return list;
    }
}
