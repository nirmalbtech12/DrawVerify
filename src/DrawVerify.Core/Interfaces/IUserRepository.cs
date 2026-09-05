using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IUserRepository
{
    Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
    Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default);
    Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default);
    async Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string normalizedUsername, Func<UserAccount, bool> verifier, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var account=await FindByUsernameAsync(normalizedUsername,cancellationToken);
        if(account is null)return new(null,false,"InvalidUsername");
        if(account.LockedUntilUtc>now)return new(null,true,"AccountLocked");
        return await RecordLoginAttemptAsync(normalizedUsername,verifier(account),lockoutThreshold,lockoutDuration,now,cancellationToken);
    }
}
public sealed record LoginAttemptState(UserAccount? Account, bool IsLocked, string? FailureReason);
