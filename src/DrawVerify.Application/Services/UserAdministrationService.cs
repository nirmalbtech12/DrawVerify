using System.Security.Cryptography;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;
using System.Text.Json;

namespace DrawVerify.Application.Services;

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
    public string? CurrentUsername => _session.CurrentUser?.Username;
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
            await _users.CreateAsync(account, new(actor.Username, username, DateTimeOffset.UtcNow, "UserCreated", "Success", RolesAfter: JsonSerializer.Serialize(account.Roles)), cancellationToken);
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

    public async Task<AuthenticationResult> UpdateProfileAsync(string username, string? displayName, string? email, bool passwordReset, CancellationToken cancellationToken = default, string? newPassword = null)
    {
        var actor = _session.CurrentUser; if (actor is null || !_authorization.IsAllowed(actor, RolePermission.ManageUsersAndRoles)) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can modify users.");
        var normalized = Normalize(username); var current = await _users.FindByUsernameAsync(normalized, cancellationToken); if (current is null) return new(AuthenticationResultKind.ValidationFailure, "The target user was not found.");
        var fields = new List<string>(); var oldValues = new Dictionary<string, string?>(); var newValues = new Dictionary<string, string?>();
        if (displayName is not null && !string.Equals(current.DisplayName, displayName.Trim(), StringComparison.Ordinal)) { fields.Add("DisplayName"); oldValues["DisplayName"] = current.DisplayName; newValues["DisplayName"] = displayName.Trim(); }
        if (!string.Equals(current.Email, email, StringComparison.Ordinal)) { fields.Add("Email"); oldValues["Email"] = current.Email; newValues["Email"] = email; }
        if (passwordReset) fields.Add("PasswordReset");
        byte[]? salt = null; byte[]? hash = null;
        if (passwordReset)
        {
            // The legacy overload has no password argument. Keep that API usable for
            // existing callers while still replacing the stored password material;
            // the UI/request overload always supplies the administrator-selected value.
            var resetPassword = newPassword ?? $"Reset-{Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))}aA1!";
            var policyError = _configuration.ValidatePassword(resetPassword); if (policyError is not null) return new(AuthenticationResultKind.ValidationFailure, policyError);
            salt = RandomNumberGenerator.GetBytes(16); hash = Rfc2898DeriveBytes.Pbkdf2(resetPassword, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        }
        if (fields.Count == 0) return new(AuthenticationResultKind.ValidationFailure, "No profile changes were supplied.");
        var audit = new AuthenticationAuditEntry(actor.Username, normalized, DateTimeOffset.UtcNow, "UserModified", "Success", ChangedFields: JsonSerializer.Serialize(fields), OldValues: JsonSerializer.Serialize(oldValues), NewValues: JsonSerializer.Serialize(newValues));
        if (passwordReset)
        {
            await _users.UpdateProfileAsync(normalized, newValues.TryGetValue("DisplayName", out var name) ? name : current.DisplayName, newValues.TryGetValue("Email", out var mail) ? mail : current.Email, true, salt, hash, PasswordIterations, audit, cancellationToken);
        }
        else
        {
            // Preserve the original interface path for profile-only updates so
            // repositories that do not need password material remain compatible.
            await _users.UpdateProfileAsync(normalized, newValues.TryGetValue("DisplayName", out var name) ? name : current.DisplayName, newValues.TryGetValue("Email", out var mail) ? mail : current.Email, false, audit, cancellationToken);
        }
        return new(AuthenticationResultKind.Success, "User profile updated.");
    }
    public Task<AuthenticationResult> UpdateProfileAsync(UserProfileUpdateRequest request, CancellationToken cancellationToken = default)
        => UpdateProfileAsync(request.Username, request.DisplayName, request.Email, request.PasswordReset, cancellationToken, request.NewPassword);
    public async Task<AuthenticationResult> DeactivateUserAsync(string username, string? reason, CancellationToken cancellationToken = default)
    {
        var actor = _session.CurrentUser; if (actor is null || !_authorization.IsAllowed(actor, RolePermission.ManageUsersAndRoles)) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can deactivate users.");
        var normalized = Normalize(username); if (await _users.FindByUsernameAsync(normalized, cancellationToken) is null) return new(AuthenticationResultKind.ValidationFailure, "The target user was not found.");
        await _users.DeactivateAsync(normalized, reason, new(actor.Username, normalized, DateTimeOffset.UtcNow, "UserDeactivated", "Success", DeactivationReason: reason), cancellationToken); return new(AuthenticationResultKind.Success, "User deactivated.");
    }
    public Task<AuthenticationResult> DeactivateUserAsync(UserDeactivationRequest request, CancellationToken cancellationToken = default)
        => DeactivateUserAsync(request.Username, request.Reason, cancellationToken);
}

public sealed class DuplicateUsernameException : Exception
{
    public DuplicateUsernameException() : base("The normalized username is already in use.") { }
}
