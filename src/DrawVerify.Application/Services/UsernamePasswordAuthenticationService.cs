using System.Security.Cryptography;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class UsernamePasswordAuthenticationService
{
    private const string GenericFailure = "Invalid username or password.";
    private readonly IUserRepository _users; private readonly IAuthenticationAuditRepository _audit; private readonly ISessionAuthenticationService _session; private readonly AuthenticationConfiguration _configuration; private readonly ICentralizedLogger _logger;
    public UsernamePasswordAuthenticationService(IUserRepository users, IAuthenticationAuditRepository audit, ISessionAuthenticationService session, AuthenticationConfiguration configuration, ICentralizedLogger logger) => (_users, _audit, _session, _configuration, _logger) = (users, audit, session, configuration, logger);
    public async Task<AuthenticationResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var normalized = UserAdministrationService.Normalize(username);
        string reason = "InvalidUsername"; AuthenticationResult result;
        try
        {
            var state = await _users.VerifyAndRecordLoginAttemptAsync(normalized, account => { var actual=Rfc2898DeriveBytes.Pbkdf2(password,account.PasswordSalt,account.PasswordIterations,HashAlgorithmName.SHA256,account.PasswordHash.Length); return CryptographicOperations.FixedTimeEquals(actual,account.PasswordHash); }, _configuration.LockoutThreshold, _configuration.LockoutDuration, DateTimeOffset.UtcNow, cancellationToken);
            if(state.Account is null) { reason=state.FailureReason??"InvalidUsername"; result=new(state.IsLocked?AuthenticationResultKind.AccountLocked:AuthenticationResultKind.InvalidCredentials,GenericFailure); }
            else if(state.FailureReason=="AccountLocked") { reason="AccountLocked"; result=new(AuthenticationResultKind.AccountLocked,GenericFailure); }
            else if(state.FailureReason=="InvalidPassword") { reason="InvalidPassword"; result=new(AuthenticationResultKind.InvalidCredentials,GenericFailure); }
            else result=new(AuthenticationResultKind.Success,"Login successful.",new AuthenticatedUser(state.Account.Username,state.Account.Roles,state.Account.RoleVersion));
            var auditReason = result.Succeeded ? null : reason == string.Empty ? "InvalidPassword" : reason;
            await _audit.WriteAsync(new(username, normalized, DateTimeOffset.UtcNow, "Login", result.Succeeded ? "Success" : "Failure", auditReason), cancellationToken);
            if (result.Succeeded) _session.SignIn(result.User!);
            return result;
        }
        catch (Exception ex) { _logger.Log(ex, "Authentication", $"Unable to complete login attempt for '{normalized}'."); throw; }
    }
    public async Task<AuthenticationResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated) return new(AuthenticationResultKind.ValidationFailure, "No authenticated session exists.");
        var user = _session.CurrentUser!;
        try { await _audit.WriteAsync(new(user.Username, user.Username, DateTimeOffset.UtcNow, "Logout", "Success"), cancellationToken); _session.SignOut(); return new(AuthenticationResultKind.Success, "Logged out."); }
        catch (Exception ex) { _logger.Log(ex, "Authentication logout", $"Unable to record logout for '{user.Username}'."); throw; }
    }
}
