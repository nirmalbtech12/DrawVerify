using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class FinalApprovalViewModelTests
{
    [Fact(DisplayName = "T08 T11 T14 role-aware approval navigation and denial")]
    public void T08_T11_T14_approver_is_allowed_and_other_roles_are_denied()
    {
        var authorization = new RoleAuthorization();
        Assert.True(new ScreenAccessGuard(new Session(new AuthenticatedUser("a", new[] { SystemRoles.Approver })), authorization).CanAccess(ScreenRegistry.Workflow));
        Assert.False(new ScreenAccessGuard(new Session(new AuthenticatedUser("v", new[] { SystemRoles.Viewer })), authorization).CanAccess(ScreenRegistry.Workflow));
        Assert.False(new ScreenAccessGuard(new Session(null), authorization).CanAccess(ScreenRegistry.Workflow));
    }
    private sealed class Session(AuthenticatedUser? user) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticated => CurrentUser is not null; public void SignIn(AuthenticatedUser value) { } public void SignOut() { }
    }
}
