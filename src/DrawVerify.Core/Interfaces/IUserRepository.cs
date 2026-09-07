using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IUserRepository
{
    Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
    Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default);
    Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string normalizedUsername, Func<UserAccount, bool> verifier, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserAccount>>(Array.Empty<UserAccount>());
    Task ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default) => Task.FromException(new NotSupportedException("Role assignment is not supported by this user repository."));
    async Task<RoleReplacementResult> ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, long expectedRoleVersion, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default)
    {
        await ReplaceRolesAsync(normalizedUsername, roles, audit, cancellationToken);
        return new(true, RoleVersion: expectedRoleVersion + 1);
    }
}
public sealed record LoginAttemptState(UserAccount? Account, bool IsLocked, string? FailureReason);
