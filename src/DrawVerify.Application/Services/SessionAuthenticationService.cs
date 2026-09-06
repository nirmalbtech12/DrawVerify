using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class SessionAuthenticationService : ISessionAuthenticationService
{
    public AuthenticatedUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public void SignIn(AuthenticatedUser user) => CurrentUser = new AuthenticatedUser(user.Username, user.Roles.ToArray());
    public void SignOut() => CurrentUser = null;
}
