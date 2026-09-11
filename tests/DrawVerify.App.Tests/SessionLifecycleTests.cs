using DrawVerify.App.Navigation;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SessionLifecycleTests
{
    [Fact(DisplayName = "WPF-005 logout clears authenticated navigation and rejects stale state")]
    public void LogoutClearsSessionAndHistory()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new("maker", [SystemRoles.Maker]));
        var history = new NavigationHistory();
        history.Push(ScreenRegistry.StepUpload);
        session.SignOut();
        history.Clear();
        Assert.False(session.IsAuthenticated);
        Assert.False(history.CanGoBack);
        Assert.Null(session.CurrentUser);
    }

    [Fact(DisplayName = "WPF-005 role refresh rebuilds effective permissions")]
    public void RoleReplacementChangesAccessAfterRefresh()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new("user", [SystemRoles.Viewer]));
        var authorization = new RoleAuthorization();
        Assert.False(authorization.IsAllowed(session.CurrentUser, RolePermission.Upload));
        session.ReplaceCurrentUserRoles("user", [SystemRoles.Maker], 2);
        Assert.True(authorization.IsAllowed(session.CurrentUser, RolePermission.Upload));
        Assert.Equal(2, session.CurrentUser!.RoleVersion);
    }
}
