using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ReportTemplateConfigurationNavigationTests
{
    [Fact(DisplayName = "TSD-AC01 Admin report-template navigation is registered and guarded")]
    public void Admin_menu_and_guard_expose_report_template_configuration()
    {
        var definition = Assert.Single(ScreenRegistry.Menus.Single(x => x.Header == "_Admin").Children!, x => x.Target == ScreenRegistry.ReportTemplateConfiguration);
        Assert.Equal(RolePermission.ManageGlobalConfiguration, definition.Permission);
        Assert.Equal("Report Template Configuration", ScreenRegistry.Title(ScreenRegistry.ReportTemplateConfiguration));

        var admin = new SessionAuthenticationService();
        admin.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
        var viewer = new SessionAuthenticationService();
        viewer.SignIn(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }));
        Assert.True(new ScreenAccessGuard(admin, new RoleAuthorization()).CanAccess(ScreenRegistry.ReportTemplateConfiguration));
        Assert.False(new ScreenAccessGuard(viewer, new RoleAuthorization()).CanAccess(ScreenRegistry.ReportTemplateConfiguration));
        Assert.False(new ScreenAccessGuard(new SessionAuthenticationService(), new RoleAuthorization()).CanAccess(ScreenRegistry.ReportTemplateConfiguration));
    }
}
