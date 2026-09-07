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
    Task<RoleReplacementResult> ReplaceRolesAsync(string normalizedUsername, IReadOnlyList<string> roles, long expectedRoleVersion, RoleAssignmentAuditEntry audit, CancellationToken cancellationToken = default)
        => Task.FromException<RoleReplacementResult>(new NotSupportedException("Versioned atomic role replacement is not supported by this user repository."));
    Task UpdateProfileAsync(string normalizedUsername, string? displayName, string? email, bool passwordReset, byte[]? passwordSalt, byte[]? passwordHash, int passwordIterations, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("Profile updates are not supported by this user repository."));
    Task UpdateProfileAsync(string normalizedUsername, string? displayName, string? email, bool passwordReset, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
        => UpdateProfileAsync(normalizedUsername, displayName, email, passwordReset, null, null, 0, audit, cancellationToken);
    Task DeactivateAsync(string normalizedUsername, string? reason, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("Deactivation is not supported by this user repository."));
}
public sealed record LoginAttemptState(UserAccount? Account, bool IsLocked, string? FailureReason);
