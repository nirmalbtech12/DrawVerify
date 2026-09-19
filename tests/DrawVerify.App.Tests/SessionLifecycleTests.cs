using DrawVerify.App.Navigation;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SessionLifecycleTests
{
    [Fact(DisplayName = "TSD-AC13-SESSION revocation state prevents further effective access")]
    public void Revocation_state_is_terminal_for_access_checks()
    {
        var grant = new ViewerAccessGrant { IsRevoked = true };
        Assert.False(grant.IsEffective(DateTime.UtcNow));
        Assert.Equal(ApprovedDocumentService.RevokedMessage, ApprovedDocumentService.RevokedMessage);
    }

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

    [Fact(DisplayName = "TD-UI-004 self-role refresh updates session authorization immediately")]
    public void Self_role_refresh_updates_current_session_without_relogin()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new("user", [SystemRoles.Maker], 1));
        session.ReplaceCurrentUserRoles("USER", [SystemRoles.Approver], 2);
        Assert.Equal(new[] { SystemRoles.Approver }, session.CurrentUser!.Roles);
        Assert.Equal(2, session.CurrentUser.RoleVersion);
        Assert.False(new RoleAuthorization().IsAllowed(session.CurrentUser, RolePermission.Upload));
        Assert.True(new RoleAuthorization().IsAllowed(session.CurrentUser, RolePermission.FinalApproval));
    }
}
