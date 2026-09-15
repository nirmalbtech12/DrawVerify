using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class WorkflowConfigurationNavigationTests
{
    [Theory(DisplayName = "TD-UI-002 only an authorized Admin can see and access workflow configuration")]
    [InlineData(SystemRoles.Admin, true, true)]
    [InlineData(SystemRoles.Maker, false, false)]
    [InlineData(SystemRoles.Checker, false, false)]
    [InlineData(SystemRoles.Viewer, false, false)]
    public void Configuration_navigation_is_guarded(string role, bool menuVisible, bool accessAllowed)
    {
        Sta.Run(() =>
        {
            var session = new Session(new("user", [role]));
            var guard = new ScreenAccessGuard(session, new RoleAuthorization());
            var menus = new RoleMenuBuilder(session, new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { });
            var admin = menus.FirstOrDefault(x => x.Header?.ToString() == "_Admin");
            var configuration = admin?.Items.Cast<System.Windows.Controls.MenuItem>().FirstOrDefault(x => x.Header?.ToString() == "_Workflow Configuration");
            Assert.Equal(menuVisible, configuration is not null); Assert.Equal(accessAllowed, guard.CanAccess(ScreenRegistry.WorkflowConfiguration)); Assert.True(guard.CanAccess(ScreenRegistry.Workflow) || role == SystemRoles.Viewer);
            Assert.Equal("Workflow Configuration", ScreenRegistry.Title(ScreenRegistry.WorkflowConfiguration));
        });
    }

    [Fact(DisplayName = "TD-UI-002 absent authentication cannot directly navigate")]
    public void Missing_context_is_denied()
    { var session = new Session(null, false); Assert.False(new ScreenAccessGuard(session, new RoleAuthorization()).CanAccess(ScreenRegistry.WorkflowConfiguration)); }

    private sealed class Session : ISessionAuthenticationService
    {
        private readonly bool _authenticated;
        public Session(AuthenticatedUser? user, bool authenticated = true) { CurrentUser = user; _authenticated = authenticated; }
        public AuthenticatedUser? CurrentUser { get; }
        public bool IsAuthenticated => _authenticated && CurrentUser is not null;
        public void SignIn(AuthenticatedUser value) { }
        public void SignOut() { }
    }
}
