using System.Globalization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteInternalSharedLinkRepository : IInternalSharedLinkRepository
{
    private readonly string _connectionString;
    public SqliteInternalSharedLinkRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        Initialize();
    }

    public async Task<InternalSharedLink> CreateAsync(InternalSharedLink link, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO InternalSharedLink(LinkId,DocumentId,VersionId,TokenHash,Url,AccessLevel,IsActive,ExpiresUtc,TrackingEnabled,CreatedUtc,RevokedUtc) VALUES($id,$document,$version,$token,$url,$level,$active,$expires,$tracking,$created,$revoked)";
        Add(command, "$id", link.LinkId); Add(command, "$document", link.DocumentId); Add(command, "$version", link.VersionId); Add(command, "$token", link.TokenHash); Add(command, "$url", link.Url); Add(command, "$level", (int)link.AccessLevel); Add(command, "$active", link.IsActive ? 1 : 0); Add(command, "$expires", link.ExpiresUtc); Add(command, "$tracking", link.TrackingEnabled ? 1 : 0); Add(command, "$created", link.CreatedUtc); Add(command, "$revoked", link.RevokedUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return link;
    }

    public async Task<InternalSharedLink?> GetByIdAsync(Guid linkId, CancellationToken cancellationToken = default)
        => await FindAsync("WHERE LinkId=$id", "$id", linkId.ToString("D"), cancellationToken).ConfigureAwait(false);

    public async Task<InternalSharedLink?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
        => await FindAsync("WHERE TokenHash=$token", "$token", tokenHash, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<InternalSharedLink>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT LinkId,DocumentId,VersionId,TokenHash,Url,AccessLevel,IsActive,ExpiresUtc,TrackingEnabled,CreatedUtc,RevokedUtc FROM InternalSharedLink WHERE DocumentId=$document AND ($version IS NULL OR VersionId=$version) ORDER BY CreatedUtc";
        Add(command, "$document", documentId); Add(command, "$version", versionId); var result = new List<InternalSharedLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader)); return result;
    }

    public async Task RevokeAsync(Guid linkId, DateTime revokedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE InternalSharedLink SET IsActive=0,RevokedUtc=$revoked WHERE LinkId=$id AND IsActive=1"; Add(command, "$id", linkId); Add(command, "$revoked", revokedUtc); if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("The shared link is missing or already inactive.");
    }

    public async Task<InternalSharedLink> RegenerateAsync(Guid oldLinkId, InternalSharedLink replacement, DateTime revokedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken); try
        {
            await using (var revoke = connection.CreateCommand()) { revoke.Transaction = (SqliteTransaction)transaction; revoke.CommandText = "UPDATE InternalSharedLink SET IsActive=0,RevokedUtc=$revoked WHERE LinkId=$old AND IsActive=1"; Add(revoke, "$old", oldLinkId); Add(revoke, "$revoked", revokedUtc); if (await revoke.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("The shared link is missing or already inactive."); }
            await using (var create = connection.CreateCommand()) { create.Transaction = (SqliteTransaction)transaction; create.CommandText = "INSERT INTO InternalSharedLink(LinkId,DocumentId,VersionId,TokenHash,Url,AccessLevel,IsActive,ExpiresUtc,TrackingEnabled,CreatedUtc,RevokedUtc) VALUES($id,$document,$version,$token,$url,$level,$active,$expires,$tracking,$created,$revoked)"; Add(create, "$id", replacement.LinkId); Add(create, "$document", replacement.DocumentId); Add(create, "$version", replacement.VersionId); Add(create, "$token", replacement.TokenHash); Add(create, "$url", replacement.Url); Add(create, "$level", (int)replacement.AccessLevel); Add(create, "$active", replacement.IsActive ? 1 : 0); Add(create, "$expires", replacement.ExpiresUtc); Add(create, "$tracking", replacement.TrackingEnabled ? 1 : 0); Add(create, "$created", replacement.CreatedUtc); Add(create, "$revoked", replacement.RevokedUtc); await create.ExecuteNonQueryAsync(cancellationToken); }
            await transaction.CommitAsync(cancellationToken); return replacement;
        } catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task RecordAccessAsync(Guid linkId, string userId, DateTime accessedUtc, CancellationToken cancellationToken = default)
    { await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO InternalSharedLinkAccess(LinkId,UserId,AccessedUtc) VALUES($id,$user,$accessed)"; Add(command, "$id", linkId); Add(command, "$user", userId); Add(command, "$accessed", accessedUtc); await command.ExecuteNonQueryAsync(cancellationToken); }

    public async Task<InternalSharedLinkUsage> GetUsageAsync(Guid linkId, bool includeUsers, CancellationToken cancellationToken = default)
    { await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*),MAX(AccessedUtc) FROM InternalSharedLinkAccess WHERE LinkId=$id"; Add(command, "$id", linkId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); var count = reader.GetInt64(0); DateTime? last = reader.IsDBNull(1) ? null : ParseDate(reader.GetString(1)); await reader.DisposeAsync(); var users = new List<string>(); if (includeUsers) { await using var usersCommand = connection.CreateCommand(); usersCommand.CommandText = "SELECT DISTINCT UserId FROM InternalSharedLinkAccess WHERE LinkId=$id ORDER BY UserId"; Add(usersCommand, "$id", linkId); await using var usersReader = await usersCommand.ExecuteReaderAsync(cancellationToken); while (await usersReader.ReadAsync(cancellationToken)) users.Add(usersReader.GetString(0)); } return new InternalSharedLinkUsage(count, last, users); }

    private async Task<InternalSharedLink?> FindAsync(string predicate, string parameter, string value, CancellationToken cancellationToken)
    { await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT LinkId,DocumentId,VersionId,TokenHash,Url,AccessLevel,IsActive,ExpiresUtc,TrackingEnabled,CreatedUtc,RevokedUtc FROM InternalSharedLink " + predicate; command.Parameters.AddWithValue(parameter, value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? Map(reader) : null; }
    private async Task<SqliteConnection> OpenAsync(CancellationToken token) { var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(token); return connection; }
    private void Initialize() { using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE IF NOT EXISTS InternalSharedLink(LinkId TEXT PRIMARY KEY,DocumentId TEXT NOT NULL,VersionId TEXT NOT NULL,TokenHash TEXT NOT NULL UNIQUE,Url TEXT NOT NULL,AccessLevel INTEGER NOT NULL,IsActive INTEGER NOT NULL,ExpiresUtc TEXT NULL,TrackingEnabled INTEGER NOT NULL,CreatedUtc TEXT NOT NULL,RevokedUtc TEXT NULL); CREATE TABLE IF NOT EXISTS InternalSharedLinkAccess(AccessId INTEGER PRIMARY KEY AUTOINCREMENT,LinkId TEXT NOT NULL,UserId TEXT NOT NULL,AccessedUtc TEXT NOT NULL,FOREIGN KEY(LinkId) REFERENCES InternalSharedLink(LinkId));"; command.ExecuteNonQuery(); }
    private static InternalSharedLink Map(SqliteDataReader r) => new() { LinkId = Guid.Parse(r.GetString(0)), DocumentId = Guid.Parse(r.GetString(1)), VersionId = Guid.Parse(r.GetString(2)), Token = r.GetString(3), Url = r.GetString(4), AccessLevel = (DrawVerify.Core.Enums.SharedLinkAccessLevel)r.GetInt32(5), IsActive = r.GetInt32(6) != 0, ExpiresUtc = r.IsDBNull(7) ? null : ParseDate(r.GetString(7)), TrackingEnabled = r.GetInt32(8) != 0, CreatedUtc = ParseDate(r.GetString(9)), RevokedUtc = r.IsDBNull(10) ? null : ParseDate(r.GetString(10)) };
    private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void Add(SqliteCommand c, string name, object? value) => c.Parameters.AddWithValue(name, value switch { Guid id => id.ToString("D"), DateTime date => date.ToString("O", CultureInfo.InvariantCulture), null => DBNull.Value, _ => (object)value });
}
