using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteViewerAccessRepository : IViewerAccessRepository
{
    private readonly string _connectionString;
    private readonly ICentralizedLogger? _logger;
    public SqliteViewerAccessRepository(string databasePath, ICentralizedLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); _logger = logger;
        try { using var connection = new SqliteConnection(_connectionString); connection.Open(); EnsureSchema(connection); }
        catch (Exception exception) { logger?.Log(exception, "Viewer access schema", "Unable to initialize viewer access persistence."); throw; }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ViewerAccessGrant (DocumentId TEXT NOT NULL, VersionId TEXT NOT NULL, TargetKind TEXT NOT NULL, TargetId TEXT NOT NULL, TargetName TEXT NOT NULL, TargetDescription TEXT NOT NULL DEFAULT '', GrantedUtc TEXT NOT NULL, GrantedByUserId TEXT NOT NULL, GrantedByName TEXT NOT NULL, ExpiresUtc TEXT NULL, IsRevoked INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(DocumentId,VersionId,TargetKind,TargetId)); CREATE INDEX IF NOT EXISTS IX_ViewerAccessGrant_Target ON ViewerAccessGrant(TargetKind,TargetId,IsRevoked); CREATE TABLE IF NOT EXISTS ViewerAccessGroup (GroupId TEXT PRIMARY KEY, DisplayName TEXT NOT NULL, Description TEXT NOT NULL DEFAULT ''); CREATE TABLE IF NOT EXISTS ViewerAccessGroupMember (GroupId TEXT NOT NULL, UserId TEXT NOT NULL, PRIMARY KEY(GroupId,UserId));";
        command.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); var result = new List<ViewerAccessTarget>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT UserId,NormalizedUsername,DisplayName FROM UserAccount WHERE IsActive=1 AND ($search='' OR NormalizedUsername LIKE $like OR DisplayName LIKE $like) ORDER BY DisplayName";
            command.Parameters.AddWithValue("$search", search.Trim()); command.Parameters.AddWithValue("$like", $"%{search.Trim()}%");
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var roles = await RolesAsync(connection, reader.GetString(0), token);
                if (roles.Any(role => IsViewerCapable(role))) result.Add(new(ViewerAccessTargetKind.User, reader.GetString(0), reader.GetString(2), string.Join(", ", roles)));
            }
        }
        foreach (var role in SystemRoles.Catalog.Where(x => IsViewerCapable(x.Id) && (string.IsNullOrWhiteSpace(search) || x.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(search, StringComparison.OrdinalIgnoreCase))))
            result.Add(new(ViewerAccessTargetKind.Role, role.Id, role.Name, role.Description));
        await using (var groups = connection.CreateCommand())
        {
            groups.CommandText = "SELECT GroupId,DisplayName,Description FROM ViewerAccessGroup WHERE $search='' OR GroupId LIKE $like OR DisplayName LIKE $like ORDER BY DisplayName";
            groups.Parameters.AddWithValue("$search", search.Trim()); groups.Parameters.AddWithValue("$like", $"%{search.Trim()}%");
            await using var reader = await groups.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) result.Add(new(ViewerAccessTargetKind.Group, reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    public async Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); var result = new List<ViewerAccessGrant>();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT TargetKind,TargetId,TargetName,TargetDescription,GrantedUtc,GrantedByUserId,GrantedByName,ExpiresUtc,IsRevoked FROM ViewerAccessGrant WHERE DocumentId=$document AND VersionId=$version ORDER BY GrantedUtc DESC"; command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", versionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadGrant(documentId, versionId, reader));
        return result;
    }

    public async Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM ViewerAccessGrant g WHERE g.DocumentId=$document AND g.VersionId=$version AND g.IsRevoked=0 AND (g.ExpiresUtc IS NULL OR g.ExpiresUtc>$now) AND (EXISTS (SELECT 1 FROM UserAccountRole viewerRole WHERE viewerRole.UserId=$user AND viewerRole.RoleName='Viewer') OR g.TargetKind='Role') AND ((g.TargetKind='User' AND g.TargetId=$user) OR (g.TargetKind='Role' AND EXISTS (SELECT 1 FROM UserAccountRole r WHERE r.UserId=$user AND r.RoleName=g.TargetId)) OR (g.TargetKind='Group' AND EXISTS (SELECT 1 FROM ViewerAccessGroupMember m WHERE m.GroupId=g.TargetId AND m.UserId=$user))) LIMIT 1 UNION SELECT 1 FROM SharedDocumentShare s WHERE s.DocumentId=$document AND s.VersionId=$version AND s.RecipientUserId=$user AND s.IsRevoked=0 LIMIT 1";
        command.Parameters.AddWithValue("$document", documentId.ToString()); command.Parameters.AddWithValue("$version", versionId.ToString()); command.Parameters.AddWithValue("$user", userId); command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        return await command.ExecuteScalarAsync(token) is not null;
    }

    public Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, AuthenticatedUser actor, CancellationToken token = default)
        => MutateAsync(documentId, versionId, change.Targets, actor, false, change.ExpiresUtc, token);

    public Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, AuthenticatedUser actor, CancellationToken token = default)
        => MutateAsync(documentId, versionId, targets, actor, true, null, token);

    private async Task<ViewerAccessMutationResult> MutateAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, AuthenticatedUser actor, bool revoke, DateTime? expiresUtc, CancellationToken token)
    {
        if (targets.Count == 0) throw new ArgumentException("At least one access target is required.", nameof(targets));
        await using var connection = await OpenAsync(token); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            EnsureApproved(connection, transaction, documentId, versionId);
            var changed = 0;
            foreach (var target in targets)
            {
                if (target.Kind == ViewerAccessTargetKind.User && !await IsViewerUserAsync(connection, transaction, target.Id, token)) throw new KeyNotFoundException($"Viewer user '{target.DisplayName}' was not found or is not Viewer-capable.");
                if (target.Kind == ViewerAccessTargetKind.Role && !SystemRoles.Catalog.Any(x => x.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase) && IsViewerCapable(x.Id))) throw new InvalidOperationException($"Role '{target.Id}' is not Viewer-capable.");
                if (target.Kind == ViewerAccessTargetKind.Group && !await ExistsAsync(connection, transaction, "SELECT 1 FROM ViewerAccessGroup WHERE GroupId=$id", target.Id, token)) throw new KeyNotFoundException($"Viewer group '{target.DisplayName}' was not found.");
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = revoke ? "UPDATE ViewerAccessGrant SET IsRevoked=1 WHERE DocumentId=$document AND VersionId=$version AND TargetKind=$kind AND TargetId=$target" : "INSERT INTO ViewerAccessGrant(DocumentId,VersionId,TargetKind,TargetId,TargetName,TargetDescription,GrantedUtc,GrantedByUserId,GrantedByName,ExpiresUtc,IsRevoked) VALUES($document,$version,$kind,$target,$name,$description,$utc,$actor,$actorName,$expires,0) ON CONFLICT(DocumentId,VersionId,TargetKind,TargetId) DO UPDATE SET TargetName=$name,TargetDescription=$description,GrantedUtc=$utc,GrantedByUserId=$actor,GrantedByName=$actorName,ExpiresUtc=$expires,IsRevoked=0";
                Add(command, "$document", documentId.ToString()); Add(command, "$version", versionId.ToString()); Add(command, "$kind", target.Kind.ToString()); Add(command, "$target", target.Id); Add(command, "$name", target.DisplayName); Add(command, "$description", target.Description); Add(command, "$utc", DateTime.UtcNow.ToString("O")); Add(command, "$actor", actor.UserId); Add(command, "$actorName", actor.DisplayName); Add(command, "$expires", (object?)expiresUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                if (await command.ExecuteNonQueryAsync(token) > 0)
                {
                    changed++;
                    await WriteAuditAsync(connection, transaction, documentId, versionId, target, actor, revoke ? DocumentAuditAction.AccessRevoked : DocumentAuditAction.AccessGranted, expiresUtc, token);
                }
            }
            await transaction.CommitAsync(token); return new(changed, Array.Empty<string>());
        }
        catch (Exception exception) { try { await transaction.RollbackAsync(token); } catch { } _logger?.Log(exception, "Viewer access mutation", $"Document={documentId}, version={versionId}, actor={actor.UserId}, revoke={revoke}."); throw; }
    }

    private static void EnsureApproved(SqliteConnection c, SqliteTransaction tx, Guid documentId, Guid versionId)
    {
        using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT 1 FROM Document d JOIN DocumentVersion v ON v.DocumentId=d.DocumentId JOIN WorkflowState w ON w.PackageId=d.DocumentId WHERE d.DocumentId=$document AND v.VersionId=$version AND d.Status=1 AND w.Status IN (11,12)"; Add(command, "$document", documentId.ToString()); Add(command, "$version", versionId.ToString()); if (command.ExecuteScalar() is null) throw new InvalidOperationException("Only approved documents can be shared with Viewers.");
    }
    private static async Task<bool> IsViewerUserAsync(SqliteConnection c, SqliteTransaction tx, string id, CancellationToken token) { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT 1 FROM UserAccountRole r JOIN UserAccount u ON u.UserId=r.UserId WHERE u.UserId=$id AND u.IsActive=1 AND r.RoleName='Viewer' LIMIT 1"; Add(command, "$id", id); return await command.ExecuteScalarAsync(token) is not null; }
    private static async Task<bool> ExistsAsync(SqliteConnection c, SqliteTransaction tx, string sql, string id, CancellationToken token) { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = sql; Add(command, "$id", id); return await command.ExecuteScalarAsync(token) is not null; }
    private static async Task<List<string>> RolesAsync(SqliteConnection c, string id, CancellationToken token) { await using var command = c.CreateCommand(); command.CommandText = "SELECT RoleName FROM UserAccountRole WHERE UserId=$id"; Add(command, "$id", id); await using var reader = await command.ExecuteReaderAsync(token); var roles = new List<string>(); while (await reader.ReadAsync(token)) roles.Add(reader.GetString(0)); return roles; }
    private static bool IsViewerCapable(string role) => role.Equals(SystemRoles.Viewer, StringComparison.OrdinalIgnoreCase);
    private static ViewerAccessGrant ReadGrant(Guid documentId, Guid versionId, SqliteDataReader reader) => new() { DocumentId = documentId, VersionId = versionId, Target = new(Enum.Parse<ViewerAccessTargetKind>(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)), GrantedUtc = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind), GrantedByUserId = reader.GetString(5), GrantedByName = reader.GetString(6), ExpiresUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind), IsRevoked = reader.GetInt32(8) != 0 };
    private static async Task WriteAuditAsync(SqliteConnection c, SqliteTransaction tx, Guid documentId, Guid versionId, ViewerAccessTarget target, AuthenticatedUser actor, DocumentAuditAction action, DateTime? expiration, CancellationToken token)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO DocumentAuditLog(AuditId,DocumentId,Action,UserId,TimestampUtc,DetailsJson,ActorRole,DocumentVersion,EventMetadataJson) VALUES($id,$document,$action,$user,$time,$details,$role,$version,$metadata)"; Add(command, "$id", Guid.NewGuid().ToString()); Add(command, "$document", documentId.ToString()); Add(command, "$action", action); Add(command, "$user", actor.UserId); Add(command, "$time", DateTime.UtcNow.ToString("O")); Add(command, "$details", JsonSerializer.Serialize(new { target.Kind, target.Id, target.DisplayName, expirationUtc = expiration })); Add(command, "$role", string.Join(",", actor.Roles)); Add(command, "$version", versionId.ToString()); Add(command, "$metadata", JsonSerializer.Serialize(new { target.Kind, target.Id, expirationUtc = expiration })); await command.ExecuteNonQueryAsync(token); }
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private async Task<SqliteConnection> OpenAsync(CancellationToken token) { var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); return c; }
}
