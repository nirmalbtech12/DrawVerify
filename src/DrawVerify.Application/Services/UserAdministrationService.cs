using System.Security.Cryptography;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

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
    public UserAdministrationService(IUserRepository users, IAuthenticationAuditRepository audit, ISessionAuthenticationService session, AuthenticationConfiguration configuration, ICentralizedLogger logger)
        => (_users, _audit, _session, _configuration, _logger) = (users, audit, session, configuration, logger);

    public async Task<AuthenticationResult> CreateUserAsync(UserCreationRequest request, CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || !_session.CurrentUser!.HasRole("Admin")) return new(AuthenticationResultKind.ValidationFailure, "Only an administrator can create users.");
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
            var account = new UserAccount { Username = username, DisplayName = request.DisplayName.Trim(), PasswordSalt = salt, PasswordHash = hash, PasswordIterations = PasswordIterations, Roles = _configuration.DefaultRoles.ToArray() };
            await _users.CreateAsync(account, new(_session.CurrentUser.Username, username, DateTimeOffset.UtcNow, "UserCreated", "Success"), cancellationToken);
            return new(AuthenticationResultKind.Success, "User created.");
        }
        catch (DuplicateUsernameException) { return new(AuthenticationResultKind.DuplicateUsername, "Username is already in use."); }
        catch (Exception ex) { _logger.Log(ex, "User creation", $"Unable to create user '{username}'."); throw; }
    }
    public static string Normalize(string username) => username.Trim().ToUpperInvariant();
}

public sealed class DuplicateUsernameException : Exception
{
    public DuplicateUsernameException() : base("The normalized username is already in use.") { }
}
