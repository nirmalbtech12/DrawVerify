using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class SessionAuthenticationService : ISessionAuthenticationService
{
    public AuthenticatedUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public void SignIn(AuthenticatedUser user) => CurrentUser = new AuthenticatedUser(user.Username, user.Roles.ToArray(), user.RoleVersion);
    public void ReplaceCurrentUserRoles(string normalizedUsername, IReadOnlyList<string> roles, long roleVersion)
    {
        if (CurrentUser is not null && string.Equals(CurrentUser.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)) CurrentUser = new AuthenticatedUser(CurrentUser.Username, roles.ToArray(), roleVersion);
    }
    public void SignOut() => CurrentUser = null;
}
