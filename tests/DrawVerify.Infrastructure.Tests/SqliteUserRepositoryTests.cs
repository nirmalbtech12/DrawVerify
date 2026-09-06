using System.Security.Cryptography;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteUserRepositoryTests
{
    [Fact(DisplayName = "TD-INFRA-01 initializes identity schema idempotently with hash-only accounts")]
    public async Task Create_persists_account_roles_and_audit_without_plaintext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-auth-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteUserRepository(path);
            _ = new SqliteUserRepository(path);
            var salt = RandomNumberGenerator.GetBytes(16); var password = "GoodPassword1!";
            var account = new UserAccount { Username="NEW.USER", DisplayName="New User", PasswordSalt=salt, PasswordHash=Rfc2898DeriveBytes.Pbkdf2(password,salt,600000,HashAlgorithmName.SHA256,32), Roles=Array.Empty<string>() };
            await repository.CreateAsync(account, new("ADMIN", "NEW.USER", DateTimeOffset.UtcNow, "UserCreated", "Success"));
            var loaded = await repository.FindByUsernameAsync("NEW.USER");
            Assert.NotNull(loaded); Assert.Empty(loaded!.Roles); Assert.Equal("PBKDF2-SHA256", loaded.PasswordAlgorithm); Assert.NotEqual(password, Convert.ToBase64String(loaded.PasswordHash));
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText="SELECT Action,ActorUsername,Username,TimestampUtc,Result FROM AuthenticationAuditLog WHERE Username='NEW.USER'";
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal("UserCreated", reader.GetString(0)); Assert.Equal("ADMIN", reader.GetString(1)); Assert.Equal("NEW.USER", reader.GetString(2)); Assert.Equal("Success", reader.GetString(4)); Assert.True(DateTimeOffset.TryParse(reader.GetString(3), out _));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Normalized_username_boundary_rejects_duplicate_and_rolls_back_second_account()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-auth-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteUserRepository(path); var first = Account("USER"); await repository.CreateAsync(first, Audit("USER"));
            var duplicate = await Assert.ThrowsAnyAsync<Exception>(() => repository.CreateAsync(Account("USER"), Audit("USER")));
            Assert.Contains("already", duplicate.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(await repository.FindByUsernameAsync("USER"));
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync(); await using var command=connection.CreateCommand(); command.CommandText="SELECT COUNT(*) FROM UserAccount"; Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Non_username_constraint_failure_is_not_translated_to_duplicate()
    {
        var path=Path.Combine(Path.GetTempPath(),$"drawverify-auth-{Guid.NewGuid():N}.db");
        try { var repository=new SqliteUserRepository(path); var first=Account("FIRST"); await repository.CreateAsync(first,Audit("FIRST")); var generated=Account("SECOND"); var second=new UserAccount { Id=first.Id, Username=generated.Username, DisplayName=generated.DisplayName, PasswordSalt=generated.PasswordSalt, PasswordHash=generated.PasswordHash }; var error=await Assert.ThrowsAnyAsync<Exception>(()=>repository.CreateAsync(second,Audit("SECOND"))); Assert.IsNotType<DuplicateUsernameException>(error); }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public async Task User_and_creation_audit_are_atomic_when_audit_insert_fails()
    {
        var path=Path.Combine(Path.GetTempPath(),$"drawverify-auth-{Guid.NewGuid():N}.db");
        try { var repository=new SqliteUserRepository(path); await using(var connection=new SqliteConnection($"Data Source={path}")){await connection.OpenAsync(); await using var command=connection.CreateCommand(); command.CommandText="CREATE TRIGGER BlockAuthAudit BEFORE INSERT ON AuthenticationAuditLog BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;"; await command.ExecuteNonQueryAsync();} await Assert.ThrowsAnyAsync<Exception>(()=>repository.CreateAsync(Account("ATOMIC"),Audit("ATOMIC"))); Assert.Null(await repository.FindByUsernameAsync("ATOMIC")); }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-001 replaces roles and durably records the complete audit")]
    public async Task Role_replacement_is_atomic_and_migration_is_idempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-roles-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteUserRepository(path);
            await repository.CreateAsync(Account("TARGET", new[] { "Maker" }), Audit("TARGET"));
            _ = new SqliteUserRepository(path);
            var now = DateTimeOffset.UtcNow;
            await repository.ReplaceRolesAsync("TARGET", new[] { "Checker", "Viewer" }, new("ADMIN", "TARGET", new[] { "Maker" }, new[] { "Checker", "Viewer" }, now, "RolesUpdated"));
            var loaded = await repository.FindByUsernameAsync("TARGET");
            Assert.Equal(new[] { "Checker", "Viewer" }, loaded!.Roles);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT ActorUsername,Username,TimestampUtc,Action,RolesBefore,RolesAfter FROM AuthenticationAuditLog WHERE Action='RolesUpdated'";
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal("ADMIN", reader.GetString(0)); Assert.Equal("TARGET", reader.GetString(1)); Assert.Equal("RolesUpdated", reader.GetString(3));
            Assert.Contains("Maker", reader.GetString(4)); Assert.Contains("Checker", reader.GetString(5)); Assert.True(DateTimeOffset.TryParse(reader.GetString(2), out _));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Role_audit_failure_rolls_back_role_replacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-roles-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteUserRepository(path);
            await repository.CreateAsync(Account("TARGET", new[] { "Maker" }), Audit("TARGET"));
            await using (var connection = new SqliteConnection($"Data Source={path}")) { await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER BlockRoleAudit BEFORE INSERT ON AuthenticationAuditLog WHEN NEW.Action='RolesUpdated' BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;"; await command.ExecuteNonQueryAsync(); }
            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceRolesAsync("TARGET", new[] { "Checker" }, new("ADMIN", "TARGET", Array.Empty<string>(), new[] { "Checker" }, DateTimeOffset.UtcNow, "RolesUpdated")));
            Assert.Equal(new[] { "Maker" }, (await repository.FindByUsernameAsync("TARGET"))!.Roles);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static UserAccount Account(string username, IReadOnlyList<string>? roles = null) { var salt=RandomNumberGenerator.GetBytes(16); return new UserAccount { Username=username,DisplayName=username,PasswordSalt=salt,PasswordHash=Rfc2898DeriveBytes.Pbkdf2("GoodPassword1!",salt,600000,HashAlgorithmName.SHA256,32),Roles=roles ?? Array.Empty<string>() }; }
    private static AuthenticationAuditEntry Audit(string username) => new("ADMIN",username,DateTimeOffset.UtcNow,"UserCreated","Success");
}
