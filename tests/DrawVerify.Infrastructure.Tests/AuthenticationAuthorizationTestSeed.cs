using System.Security.Cryptography;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DrawVerify.Infrastructure.Tests;

public sealed class AuthenticationAuthorizationTestSeed : IAsyncDisposable
{
    public const string Password = "GoodPassword1!";
    private readonly string _databasePath;

    private AuthenticationAuthorizationTestSeed(string databasePath, SqliteUserRepository repository)
        => (_databasePath, Repository) = (databasePath, repository);

    public string DatabasePath => _databasePath;
    public SqliteUserRepository Repository { get; }

    public static async Task<AuthenticationAuthorizationTestSeed> CreateAsync(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(Path.GetTempPath(), $"drawverify-auth-seed-{Guid.NewGuid():N}.db");
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authentication seed databases must be under the temporary test directory.");
        var repository = new SqliteUserRepository(path);
        var seed = new AuthenticationAuthorizationTestSeed(path, repository);
        foreach (var (username, role) in new[]
        {
            ("ADMIN", "Admin"), ("MAKER", "Maker"), ("CHECKER", "Checker"),
            ("APPROVER", "Approver"), ("TESTUSER", "Viewer")
        })
        {
            var existing = await repository.FindByUsernameAsync(username);
            if (existing is not null)
            {
                if (existing.Roles.Count != 1 || !string.Equals(existing.Roles[0], role, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Existing seed user {username} has unexpected roles.");
                continue;
            }
            var salt = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"drawverify-seed:{username}"))[..16];
            var hash = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 600_000, HashAlgorithmName.SHA256, 32);
            await repository.CreateAsync(new UserAccount
            {
                Id = Guid.Parse($"{GuidUtility.Create(username):D}"), Username = username, DisplayName = username,
                PasswordSalt = salt, PasswordHash = hash, PasswordIterations = 600_000, Roles = new[] { role }
            }, new("SEED", username, DateTimeOffset.UtcNow, "TestSeed", "Success"));
        }
        var accounts = await repository.ListAsync();
        if (accounts.Count != 5 || accounts.Any(x => x.Roles.Count != 1))
            throw new InvalidOperationException("Authentication seed did not persist exactly the five expected users.");
        return seed;
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.CompletedTask;
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private static class GuidUtility
    {
        public static Guid Create(string value)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"drawverify-seed-id:{value}"));
            return new Guid(bytes[..16]);
        }
    }
}
