using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface ISessionAuthenticationService
{
    AuthenticatedUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    void SignIn(AuthenticatedUser user);
    void ReplaceCurrentUserRoles(string normalizedUsername, IReadOnlyList<string> roles, long roleVersion) { }
    void SignOut();
}
