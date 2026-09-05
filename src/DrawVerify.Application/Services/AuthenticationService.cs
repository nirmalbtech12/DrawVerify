namespace DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Authenticates users and resolves roles/permissions via IAuthenticationService.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    public User? CurrentUser { get; private set; }
    public bool CanExtractMeasurements => CurrentUser is { IsAuthenticated: true } user &&
        (string.Equals(user.Role, "Maker", StringComparison.OrdinalIgnoreCase) || string.Equals(user.Role, "Checker", StringComparison.OrdinalIgnoreCase));
    public void SignIn(User user) => CurrentUser = user;
    public void SignOut() => CurrentUser = null;
}
