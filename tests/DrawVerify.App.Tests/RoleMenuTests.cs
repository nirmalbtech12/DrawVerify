using System.Windows.Controls;
using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class RoleMenuTests
{
    [Fact(DisplayName = "TD-UI-004 only Admin can access role administration navigation")]
    public void Role_assignment_navigation_is_admin_only()
    {
        Sta.Run(() =>
        {
            var adminMenus = new RoleMenuBuilder(new TestSession(SystemRoles.Admin), new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { });
            var userMenus = new RoleMenuBuilder(new TestSession(SystemRoles.Maker), new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { });
            var admin = Assert.Single(adminMenus, x => x.Header?.ToString() == "_Admin");
            Assert.Contains(admin.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "_Role Assignment");
            Assert.DoesNotContain(userMenus, x => x.Header?.ToString() == "_Admin");
        });
    }
    [Theory(DisplayName = "WPF-003 role menu matrix contains only permitted top-level entries")]
    [InlineData(SystemRoles.Maker, true, false, false)]
    [InlineData(SystemRoles.Checker, false, false, false)]
    [InlineData(SystemRoles.Approver, false, false, false)]
    [InlineData(SystemRoles.Admin, false, true, false)]
    [InlineData(SystemRoles.Viewer, false, false, true)]
    public void MenuVisibilityMatchesRole(string role, bool uploadVisible, bool adminVisible, bool sharedDocumentsVisible)
    {
        Sta.Run(() =>
        {
            var session = new TestSession(role);
            var menus = new RoleMenuBuilder(session, new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { }).ToArray();
            var expected = new[] { "_File", "_Verification", "_Workflow", "_Help" }
                .Concat(uploadVisible ? ["_Upload & Verify"] : [])
                .Concat(sharedDocumentsVisible ? ["_Shared Documents"] : [])
                .Concat(adminVisible ? ["_Admin"] : [])
                .OrderBy(x => x);
            Assert.Equal(expected, menus.Select(x => x.Header?.ToString()).OrderBy(x => x));
            var file = Assert.Single(menus, x => x.Header?.ToString() == "_File");
            Assert.Contains(file.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "_Dashboard / Home");
            Assert.Contains(file.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "_Logout / Exit");
        });
    }

    [Fact(DisplayName = "WPF-003 disallowed menu activation remains inert")]
    public void DisallowedUploadIsAbsentForViewer()
    {
        Sta.Run(() =>
        {
            var menus = new RoleMenuBuilder(new TestSession(SystemRoles.Viewer), new RoleAuthorization()).Build(_ => throw new Xunit.Sdk.XunitException("unexpected navigation"), () => Task.CompletedTask, _ => { });
            Assert.DoesNotContain(menus, menu => menu.Header?.ToString() == "_Upload & Verify");
        });
    }

    [Theory(DisplayName = "WPF-003 role menu children follow the approved role matrix")]
    [InlineData(SystemRoles.Maker, "_My Submissions")]
    [InlineData(SystemRoles.Checker, "_To Review")]
    [InlineData(SystemRoles.Approver, "_To Approve")]
    public void WorkflowChildIsPresentOnlyForItsRole(string role, string expectedChild)
    {
        Sta.Run(() =>
        {
            var menus = new RoleMenuBuilder(new TestSession(role), new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { });
            var workflow = Assert.Single(menus, x => x.Header?.ToString() == "_Workflow");
            Assert.Contains(workflow.Items.Cast<MenuItem>(), x => x.Header?.ToString() == expectedChild);
            Assert.Single(workflow.Items.Cast<MenuItem>());
        });
    }

    [Fact(DisplayName = "WPF-003 Admin menu contains all approved administration entries")]
    public void AdminMenuContainsApprovedEntries()
    {
        Sta.Run(() =>
        {
            var menus = new RoleMenuBuilder(new TestSession(SystemRoles.Admin), new RoleAuthorization()).Build(_ => { }, () => Task.CompletedTask, _ => { });
            var admin = Assert.Single(menus, x => x.Header?.ToString() == "_Admin");
            Assert.Equal(new[] { "_User Management", "_Role Assignment", "_Workflow Configuration", "_Report Template Configuration" }, admin.Items.Cast<MenuItem>().Select(x => x.Header?.ToString()));
        });
    }

    private sealed class TestSession(string role) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; } = new("test-user", [role]);
        public bool IsAuthenticated => true;
        public void SignIn(AuthenticatedUser user) { }
        public void SignOut() { }
    }
}
