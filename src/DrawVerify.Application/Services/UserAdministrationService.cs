using System.Security.Cryptography;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;

namespace DrawVerify.Application.Services;

public sealed record UserCreationRequest(string Username, string DisplayName, string InitialPassword);

public sealed class UserAdministrationService
{
    private const int PasswordIterations = 600_000;
    private readonly IUserRepository _users;
    private readonly IAuthenticationAuditRepository _audit;
    private readonly ISessionAuthenticationService _session;
    private readonly AuthenticationConfiguration _configuration;
    private readonly ICentralizedLogger _logger;
    private readonly RoleAuthorization _authorization;
    public UserAdministrationService(IUserRepository users, IAuthenticationAuditRepository audit, ISessionAuthenticationService session, AuthenticationConfiguration configuration, ICentralizedLogger logger, RoleAuthorization? authorization = null)
        => (_users, _audit, _session, _configuration, _logger, _authorization) = (users, audit, session, configuration, logger, authorization ?? new RoleAuthorization());
    public IReadOnlyList<string> CurrentUserRoles => _session.CurrentUser?.Roles ?? Array.Empty<string>();
    public bool CanEditRoles => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageUsersAndRoles);

    public async Task<AuthenticationResult> CreateUserAsync(UserCreationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = _session.CurrentUser;
        if (!_session.IsAuthenticated || actor is null || !_authorization.IsAllowed(actor, RolePermission.ManageUsersAndRoles)) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can create users.");
        var username = Normalize(request.Username);
        if (username.Length == 0) return new(AuthenticationResultKind.ValidationFailure, "Username is required.");
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return new(AuthenticationResultKind.ValidationFailure, "Display name is required.");
        if (string.IsNullOrEmpty(request.InitialPassword)) return new(AuthenticationResultKind.ValidationFailure, "Initial password is required.");
        var policyError = _configuration.ValidatePassword(request.InitialPassword);
        if (policyError is not null) return new(AuthenticationResultKind.ValidationFailure, policyError);
        try
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(request.InitialPassword, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
            var account = new UserAccount { Username = username, DisplayName = request.DisplayName.Trim(), PasswordSalt = salt, PasswordHash = hash, PasswordIterations = PasswordIterations, Roles = _configuration.CanonicalDefaultRoles };
            await _users.CreateAsync(account, new(actor.Username, username, DateTimeOffset.UtcNow, "UserCreated", "Success"), cancellationToken);
            return new(AuthenticationResultKind.Success, "User created.");
        }
        catch (DuplicateUsernameException) { return new(AuthenticationResultKind.DuplicateUsername, "Username is already in use."); }
        catch (Exception ex) { _logger.Log(ex, "User creation", $"Unable to create user '{username}'."); throw; }
    }
    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || !_authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageUsersAndRoles)) throw new UnauthorizedAccessException("Only an administrator can view user roles.");
        return await _users.ListAsync(cancellationToken);
    }
    public async Task<AuthenticationResult> SetUserRolesAsync(string username, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var actor = _session.CurrentUser;
        if (!_session.IsAuthenticated || actor is null || !_authorization.IsAllowed(actor, RolePermission.ManageUsersAndRoles)) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can change user roles.");
        var current = await _users.FindByUsernameAsync(Normalize(username), cancellationToken);
        if (current is null) return new(AuthenticationResultKind.ValidationFailure, "The target user was not found.");
        return await SetUserRolesAsync(username, roles, current.RoleVersion, cancellationToken);
    }

    public async Task<AuthenticationResult> SetUserRolesAsync(string username, IEnumerable<string> roles, long expectedRoleVersion, CancellationToken cancellationToken = default)
    {
        var actor = _session.CurrentUser;
        if (!_session.IsAuthenticated || actor is null || !_authorization.IsAllowed(actor, RolePermission.ManageUsersAndRoles)) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can change user roles.");
        var normalized = Normalize(username);
        IReadOnlyList<string> canonical;
        try { canonical = SystemRoles.ValidateAndCanonicalize(roles); } catch (InvalidOperationException ex) { return new(AuthenticationResultKind.ValidationFailure, ex.Message); }
        try
        {
            var result = await _users.ReplaceRolesAsync(normalized, canonical, expectedRoleVersion, new(actor.Username, normalized, Array.Empty<string>(), canonical, DateTimeOffset.UtcNow, "RolesUpdated"), cancellationToken);
            if (!result.Succeeded) return new(AuthenticationResultKind.ValidationFailure, result.Message ?? "The role update was not saved.");
            if (string.Equals(actor.Username, normalized, StringComparison.OrdinalIgnoreCase)) _session.ReplaceCurrentUserRoles(normalized, canonical, result.RoleVersion);
            return new(AuthenticationResultKind.Success, "User roles updated successfully.");
        }
        catch (KeyNotFoundException) { return new(AuthenticationResultKind.ValidationFailure, "The target user was not found."); }
        catch (Exception ex) { _logger.Log(ex, "Role assignment", $"Unable to update roles for '{normalized}'."); throw; }
    }
    public static string Normalize(string username) => username.Trim().ToUpperInvariant();
}

public sealed class DuplicateUsernameException : Exception
{
    public DuplicateUsernameException() : base("The normalized username is already in use.") { }
}
