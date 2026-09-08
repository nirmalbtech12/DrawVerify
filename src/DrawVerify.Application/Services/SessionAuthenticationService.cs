using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class SessionAuthenticationService : ISessionAuthenticationService
{
    public AuthenticatedUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public void SignIn(AuthenticatedUser user) => CurrentUser = Copy(user);
    public void ReplaceCurrentUserRoles(string normalizedUsername, IReadOnlyList<string> roles, long roleVersion)
    {
        if (CurrentUser is not null && string.Equals(CurrentUser.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)) CurrentUser = Copy(CurrentUser, roles, roleVersion);
    }
    public void SignOut() => CurrentUser = null;
    private static AuthenticatedUser Copy(AuthenticatedUser user, IReadOnlyList<string>? roles = null, long? roleVersion = null) => new(user.Username, (roles ?? user.Roles).ToArray(), roleVersion ?? user.RoleVersion) { UserId = user.UserId, DisplayName = user.DisplayName };
}
