using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

/// <summary>
/// Authenticates users and resolves roles/permissions via IAuthenticationService.
/// </summary>
public sealed class AuthenticationService
{
    private readonly IAuthenticationService _authentication;
    public AuthenticationService(IAuthenticationService authentication) => _authentication = authentication;
    public User? CurrentUser => _authentication.CurrentUser;
    public bool CanUploadStep => _authentication.IsAuthenticated && _authentication.IsMaker;
}
