using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IUserRepository
{
    Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
    Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default);
    Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default);
}
public sealed record LoginAttemptState(UserAccount? Account, bool IsLocked, string? FailureReason);
