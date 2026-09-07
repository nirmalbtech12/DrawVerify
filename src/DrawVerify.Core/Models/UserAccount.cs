namespace DrawVerify.Core.Models;

public sealed class UserAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required byte[] PasswordSalt { get; init; }
    public required byte[] PasswordHash { get; init; }
    public string PasswordAlgorithm { get; init; } = "PBKDF2-SHA256";
    public int PasswordIterations { get; init; } = 600_000;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public long RoleVersion { get; init; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}
