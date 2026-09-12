using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DrawVerify.Infrastructure.Persistence;

public sealed class SqliteUserRepository : IUserRepository
{
    private readonly string _connectionString;
    private readonly ICentralizedLogger? _logger;
    public SqliteUserRepository(string databasePath, ICentralizedLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); _logger=logger;
        try { Initialize(); } catch (Exception ex) { logger?.Log(ex, "SQLite identity startup", "Unable to initialize identity schema"); throw; }
    }
    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE IF NOT EXISTS UserAccount (UserId TEXT PRIMARY KEY, NormalizedUsername TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL, PasswordSalt BLOB NOT NULL, PasswordHash BLOB NOT NULL, PasswordAlgorithm TEXT NOT NULL, PasswordIterations INTEGER NOT NULL, FailedAttempts INTEGER NOT NULL DEFAULT 0, LockedUntilUtc TEXT NULL, RoleVersion INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS UserAccountRole (UserId TEXT NOT NULL, RoleName TEXT NOT NULL, PRIMARY KEY(UserId, RoleName), FOREIGN KEY(UserId) REFERENCES UserAccount(UserId));"; cmd.ExecuteNonQuery();
        SqliteAuthenticationAuditRepository.EnsureSchema(c);
        AddColumnIfMissing(c, "UserAccount", "RoleVersion", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "UserAccount", "Email", "TEXT NULL");
        AddColumnIfMissing(c, "UserAccount", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(c, "UserAccount", "DeactivationReason", "TEXT NULL");
    }
    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken);
        await using var q = c.CreateCommand(); q.CommandText = "SELECT NormalizedUsername FROM UserAccount ORDER BY NormalizedUsername";
        await using var r = await q.ExecuteReaderAsync(cancellationToken); var result = new List<UserAccount>();
        while (await r.ReadAsync(cancellationToken)) { var account = await FindByUsernameAsync(r.GetString(0), cancellationToken); if (account is not null) result.Add(account); }
        return result;
    }
    public async Task ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default)
    {
        var current = await FindByUsernameAsync(normalizedUsername, cancellationToken);
        if (current is null) throw new KeyNotFoundException($"User '{normalizedUsername}' was not found.");
        var result = await ReplaceRolesAsync(normalizedUsername, roles, current.RoleVersion, audit, cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
    }

    public async Task<RoleReplacementResult> ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, long expectedRoleVersion, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> canonicalRoles;
        try { canonicalRoles = Core.Authorization.SystemRoles.ValidateAndCanonicalize(roles).OrderBy(role => role, StringComparer.Ordinal).ToArray(); }
        catch (InvalidOperationException ex) { return new(false, RoleReplacementFailure.InvalidRoles, ex.Message, expectedRoleVersion); }
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var find = c.CreateCommand(); find.Transaction = tx; find.CommandText = "SELECT UserId,RoleVersion FROM UserAccount WHERE NormalizedUsername=$u"; find.Parameters.AddWithValue("$u", normalizedUsername);
            await using var found = await find.ExecuteReaderAsync(cancellationToken);
            if (!await found.ReadAsync(cancellationToken)) { await tx.RollbackAsync(cancellationToken); return new(false, RoleReplacementFailure.UserNotFound, $"User '{normalizedUsername}' was not found."); }
            var id = found.GetString(0); var currentVersion = found.GetInt64(1); await found.DisposeAsync();
            if (currentVersion != expectedRoleVersion) { await tx.RollbackAsync(cancellationToken); return new(false, RoleReplacementFailure.StaleRoleVersion, "The user's roles changed while you were editing. Reload and try again.", currentVersion); }
            var before = new List<string>();
            await using (var readRoles = c.CreateCommand()) { readRoles.Transaction = tx; readRoles.CommandText = "SELECT RoleName FROM UserAccountRole WHERE UserId=$id ORDER BY RoleName"; readRoles.Parameters.AddWithValue("$id", id); await using var reader = await readRoles.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) before.Add(reader.GetString(0)); }
            if (before.Contains(Core.Authorization.SystemRoles.Admin, StringComparer.OrdinalIgnoreCase) && !canonicalRoles.Contains(Core.Authorization.SystemRoles.Admin, StringComparer.OrdinalIgnoreCase))
            {
                await using var count = c.CreateCommand(); count.Transaction = tx; count.CommandText = "SELECT COUNT(*) FROM UserAccountRole WHERE RoleName=$role"; count.Parameters.AddWithValue("$role", Core.Authorization.SystemRoles.Admin);
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken)) <= 1) { await tx.RollbackAsync(cancellationToken); return new(false, RoleReplacementFailure.LastAdministrator, "At least one administrator must remain.", currentVersion); }
            }
            if (before.Contains(Core.Authorization.SystemRoles.Approver, StringComparer.OrdinalIgnoreCase) && !canonicalRoles.Contains(Core.Authorization.SystemRoles.Approver, StringComparer.OrdinalIgnoreCase))
            {
                await using var count = c.CreateCommand(); count.Transaction = tx; count.CommandText = "SELECT COUNT(*) FROM UserAccountRole WHERE RoleName=$role"; count.Parameters.AddWithValue("$role", Core.Authorization.SystemRoles.Approver);
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken)) <= 1) { await tx.RollbackAsync(cancellationToken); return new(false, RoleReplacementFailure.LastApprover, "At least one Approver must remain.", currentVersion); }
            }
            if (before.SequenceEqual(canonicalRoles, StringComparer.OrdinalIgnoreCase)) { await tx.RollbackAsync(cancellationToken); return new(true, RoleVersion: currentVersion); }
            await Execute(c, tx, "DELETE FROM UserAccountRole WHERE UserId=$id", cancellationToken, ("$id", id));
            foreach (var role in canonicalRoles) await Execute(c, tx, "INSERT INTO UserAccountRole (UserId,RoleName) VALUES ($id,$r)", cancellationToken, ("$id", id), ("$r", role));
            await Execute(c, tx, "UPDATE UserAccount SET RoleVersion=RoleVersion+1 WHERE UserId=$id AND RoleVersion=$version", cancellationToken, ("$id", id), ("$version", expectedRoleVersion));
            var added = canonicalRoles.Except(before, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var removed = before.Except(canonicalRoles, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var action = added.Length > 0 && removed.Length == 0 ? "RoleAssigned" : removed.Length > 0 && added.Length == 0 ? "RoleRemoved" : "RolesUpdated";
            var entry = new AuthenticationAuditEntry(audit.ActorUsername, audit.AffectedUsername, audit.TimestampUtc, action, "Success", null, JsonSerializer.Serialize(before), JsonSerializer.Serialize(canonicalRoles), ChangedFields: action == "RoleAssigned" ? JsonSerializer.Serialize(added) : action == "RoleRemoved" ? JsonSerializer.Serialize(removed) : null);
            await SqliteAuthenticationAuditRepository.InsertAsync(c, tx, entry, cancellationToken); await tx.CommitAsync(cancellationToken);
            return new(true, RoleVersion: expectedRoleVersion + 1);
        }
        catch (Exception ex) { try { await tx.RollbackAsync(cancellationToken); } catch { } _logger?.Log(ex, "SQLite role replacement", $"Unable to update roles for '{normalizedUsername}'."); throw; }
    }
    public async Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken);
        UserAccount account;
        await using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT UserId,NormalizedUsername,DisplayName,Email,IsActive,DeactivationReason,PasswordSalt,PasswordHash,PasswordAlgorithm,PasswordIterations,FailedAttempts,LockedUntilUtc,RoleVersion FROM UserAccount WHERE NormalizedUsername=$u";
            q.Parameters.AddWithValue("$u", normalizedUsername);
            await using var r = await q.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken)) return null;
            account = new UserAccount { Id = Guid.Parse(r.GetString(0)), Username = r.GetString(1), DisplayName = r.GetString(2), Email = r.IsDBNull(3) ? null : r.GetString(3), IsActive = r.GetInt32(4) != 0, DeactivationReason = r.IsDBNull(5) ? null : r.GetString(5), PasswordSalt = (byte[])r[6], PasswordHash = (byte[])r[7], PasswordAlgorithm = r.GetString(8), PasswordIterations = r.GetInt32(9), FailedAttempts = r.GetInt32(10), LockedUntilUtc = r.IsDBNull(11) ? null : DateTimeOffset.Parse(r.GetString(11)), RoleVersion = r.GetInt64(12) };
        }
        var list = new List<string>();
        await using (var roles = c.CreateCommand())
        {
            roles.CommandText = "SELECT RoleName FROM UserAccountRole WHERE UserId=$id ORDER BY RoleName";
            roles.Parameters.AddWithValue("$id", account.Id.ToString());
            await using var rr = await roles.ExecuteReaderAsync(cancellationToken);
            while (await rr.ReadAsync(cancellationToken)) list.Add(rr.GetString(0));
        }
        return new UserAccount { Id=account.Id, Username=account.Username, DisplayName=account.DisplayName, Email=account.Email, IsActive=account.IsActive, DeactivationReason=account.DeactivationReason, PasswordSalt=account.PasswordSalt, PasswordHash=account.PasswordHash, PasswordAlgorithm=account.PasswordAlgorithm, PasswordIterations=account.PasswordIterations, FailedAttempts=account.FailedAttempts, LockedUntilUtc=account.LockedUntilUtc, RoleVersion=account.RoleVersion, Roles=list };
    }
    public async Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try { await Execute(c, tx, "INSERT INTO UserAccount (UserId,NormalizedUsername,DisplayName,PasswordSalt,PasswordHash,PasswordAlgorithm,PasswordIterations,RoleVersion) VALUES ($id,$u,$d,$s,$h,$a,$i,$v)", cancellationToken, ("$id",account.Id.ToString()),("$u",account.Username),("$d",account.DisplayName),("$s",account.PasswordSalt),("$h",account.PasswordHash),("$a",account.PasswordAlgorithm),("$i",account.PasswordIterations),("$v",account.RoleVersion)); foreach (var role in account.Roles) await Execute(c,tx,"INSERT INTO UserAccountRole (UserId,RoleName) VALUES ($id,$r)",cancellationToken,("$id",account.Id.ToString()),("$r",role)); await SqliteAuthenticationAuditRepository.InsertAsync(c,tx,audit,cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (SqliteException ex) when (IsNormalizedUsernameConflict(ex)) { await tx.RollbackAsync(cancellationToken); throw new DuplicateUsernameException(); }
        catch (Exception ex) { try { await tx.RollbackAsync(cancellationToken); } catch { } _logger?.Log(ex, "SQLite user creation", $"Unable to persist user '{account.Username}'."); throw; }
    }
    public async Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int threshold, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var read=c.CreateCommand(); read.Transaction=tx; read.CommandText="SELECT LockedUntilUtc FROM UserAccount WHERE NormalizedUsername=$u"; read.Parameters.AddWithValue("$u",normalizedUsername); var lockedValue=await read.ExecuteScalarAsync(cancellationToken); if(lockedValue is null) { await tx.CommitAsync(cancellationToken); return new(null,false,"InvalidUsername"); }
            var lockedUntil=lockedValue is DBNull ? (DateTimeOffset?)null : DateTimeOffset.Parse((string)lockedValue); if(lockedUntil > now) { await tx.CommitAsync(cancellationToken); return new(null,true,"AccountLocked"); }
            await using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText=successful ? "UPDATE UserAccount SET FailedAttempts=0,LockedUntilUtc=NULL WHERE NormalizedUsername=$u" : "UPDATE UserAccount SET FailedAttempts=FailedAttempts+1,LockedUntilUtc=CASE WHEN $t>0 AND FailedAttempts+1 >= $t THEN $until ELSE NULL END WHERE NormalizedUsername=$u"; q.Parameters.AddWithValue("$u",normalizedUsername); q.Parameters.AddWithValue("$t",threshold); q.Parameters.AddWithValue("$until",now.Add(duration).ToString("O")); await q.ExecuteNonQueryAsync(cancellationToken); await tx.CommitAsync(cancellationToken); var updated=await FindByUsernameAsync(normalizedUsername,cancellationToken); var isLocked=updated?.LockedUntilUtc > now; return new(updated,isLocked,successful?null:isLocked?"AccountLocked":"InvalidPassword");
        }
        catch(Exception ex) { try { await tx.RollbackAsync(cancellationToken); } catch { } _logger?.Log(ex,"SQLite login state mutation",$"Unable to update login state for '{normalizedUsername}'."); throw; }
    }
    public async Task UpdateProfileAsync(string normalizedUsername, string? displayName, string? email, bool passwordReset, byte[]? passwordSalt, byte[]? passwordHash, int passwordIterations, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try { await Execute(c, tx, "UPDATE UserAccount SET DisplayName=$d,Email=$e,PasswordSalt=COALESCE($s,PasswordSalt),PasswordHash=COALESCE($h,PasswordHash),PasswordIterations=CASE WHEN $i > 0 THEN $i ELSE PasswordIterations END WHERE NormalizedUsername=$u AND IsActive=1", cancellationToken, ("$d", displayName ?? string.Empty), ("$e", (object?)email ?? DBNull.Value), ("$s", (object?)passwordSalt ?? DBNull.Value), ("$h", (object?)passwordHash ?? DBNull.Value), ("$i", passwordIterations), ("$u", normalizedUsername)); await SqliteAuthenticationAuditRepository.InsertAsync(c, tx, audit, cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (Exception ex) { try { await tx.RollbackAsync(cancellationToken); } catch { } _logger?.Log(ex, "SQLite profile update", $"Unable to update profile for '{normalizedUsername}'."); throw; }
    }
    public Task UpdateProfileAsync(string normalizedUsername, string? displayName, string? email, bool passwordReset, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
        => UpdateProfileAsync(normalizedUsername, displayName, email, passwordReset, null, null, 0, audit, cancellationToken);
    public async Task DeactivateAsync(string normalizedUsername, string? reason, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try { await Execute(c, tx, "UPDATE UserAccount SET IsActive=0,DeactivationReason=$r WHERE NormalizedUsername=$u", cancellationToken, ("$r", (object?)reason ?? DBNull.Value), ("$u", normalizedUsername)); await SqliteAuthenticationAuditRepository.InsertAsync(c, tx, audit, cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (Exception ex) { try { await tx.RollbackAsync(cancellationToken); } catch { } _logger?.Log(ex, "SQLite user deactivation", $"Unable to deactivate user '{normalizedUsername}'."); throw; }
    }
    public async Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string normalizedUsername, Func<UserAccount, bool> verifier, int threshold, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var c=new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText="SELECT UserId,NormalizedUsername,DisplayName,PasswordSalt,PasswordHash,PasswordAlgorithm,PasswordIterations,FailedAttempts,LockedUntilUtc,RoleVersion FROM UserAccount WHERE NormalizedUsername=$u"; q.Parameters.AddWithValue("$u",normalizedUsername);
            await using var r=await q.ExecuteReaderAsync(cancellationToken); if(!await r.ReadAsync(cancellationToken)){await tx.CommitAsync(cancellationToken);return new(null,false,"InvalidUsername");}
            var account=new UserAccount{Id=Guid.Parse(r.GetString(0)),Username=r.GetString(1),DisplayName=r.GetString(2),PasswordSalt=(byte[])r[3],PasswordHash=(byte[])r[4],PasswordAlgorithm=r.GetString(5),PasswordIterations=r.GetInt32(6),FailedAttempts=r.GetInt32(7),LockedUntilUtc=r.IsDBNull(8)?null:DateTimeOffset.Parse(r.GetString(8)),RoleVersion=r.GetInt64(9)};
            if(account.LockedUntilUtc>now){await tx.CommitAsync(cancellationToken);return new(null,true,"AccountLocked");}
            await r.DisposeAsync(); await using var roles=c.CreateCommand(); roles.Transaction=tx; roles.CommandText="SELECT RoleName FROM UserAccountRole WHERE UserId=$id ORDER BY RoleName"; roles.Parameters.AddWithValue("$id",account.Id.ToString()); await using var rr=await roles.ExecuteReaderAsync(cancellationToken); var roleList=new List<string>(); while(await rr.ReadAsync(cancellationToken))roleList.Add(rr.GetString(0)); account= new UserAccount{Id=account.Id,Username=account.Username,DisplayName=account.DisplayName,PasswordSalt=account.PasswordSalt,PasswordHash=account.PasswordHash,PasswordAlgorithm=account.PasswordAlgorithm,PasswordIterations=account.PasswordIterations,FailedAttempts=account.FailedAttempts,LockedUntilUtc=account.LockedUntilUtc,RoleVersion=account.RoleVersion,Roles=roleList}; await rr.DisposeAsync(); var successful=verifier(account); await using var update=c.CreateCommand(); update.Transaction=tx; update.CommandText=successful?"UPDATE UserAccount SET FailedAttempts=0,LockedUntilUtc=NULL WHERE NormalizedUsername=$u":"UPDATE UserAccount SET FailedAttempts=FailedAttempts+1,LockedUntilUtc=CASE WHEN $t>0 AND FailedAttempts+1 >= $t THEN $until ELSE NULL END WHERE NormalizedUsername=$u"; update.Parameters.AddWithValue("$u",normalizedUsername); update.Parameters.AddWithValue("$t",threshold); update.Parameters.AddWithValue("$until",now.Add(duration).ToString("O")); await update.ExecuteNonQueryAsync(cancellationToken);
            var locked= !successful && threshold>0 && account.FailedAttempts+1>=threshold; account.FailedAttempts=successful?0:account.FailedAttempts+1; account.LockedUntilUtc=locked?now.Add(duration):null; await tx.CommitAsync(cancellationToken); return new(account,locked,successful?null:locked?"AccountLocked":"InvalidPassword");
        }
        catch(Exception ex){try{await tx.RollbackAsync(cancellationToken);}catch{} _logger?.Log(ex,"SQLite atomic login","Unable to verify credentials and update login state.");throw;}
    }
    private static bool IsNormalizedUsernameConflict(SqliteException ex) => ex.SqliteErrorCode == 19 && ex.Message.Contains("UserAccount.NormalizedUsername",StringComparison.OrdinalIgnoreCase);
    private static async Task Execute(SqliteConnection c, SqliteTransaction tx, string sql, CancellationToken token, params (string,object)[] values) { await using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText=sql; foreach(var (name,value) in values) q.Parameters.AddWithValue(name,value); await q.ExecuteNonQueryAsync(token); }
    private static void AddColumnIfMissing(SqliteConnection c, string table, string column, string definition)
    { using var q = c.CreateCommand(); q.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}"; try { q.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
}
