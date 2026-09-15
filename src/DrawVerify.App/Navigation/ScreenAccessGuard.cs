using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.Navigation;

public sealed class ScreenAccessGuard(ISessionAuthenticationService session, RoleAuthorization authorization)
{
    public bool CanAccess(string target)
    {
        if (!session.IsAuthenticated) return false;
        if (target is ScreenRegistry.Dashboard or ScreenRegistry.Help) return true;
        var user = session.CurrentUser;
        return target switch
        {
            ScreenRegistry.StepUpload or ScreenRegistry.DxfUpload or ScreenRegistry.UploadVerify => authorization.IsAllowed(user, RolePermission.Upload),
            ScreenRegistry.Verification => authorization.IsAllowed(user, RolePermission.ReviewVerification) || authorization.IsAllowed(user, RolePermission.StartVerification) || authorization.IsAllowed(user, RolePermission.ReadApprovedDocuments),
            ScreenRegistry.Workflow => HasRole(user, SystemRoles.Admin) || HasRole(user, SystemRoles.Maker) || HasRole(user, SystemRoles.Checker) || authorization.IsAllowed(user, RolePermission.FinalApproval),
            ScreenRegistry.UserAdministration => authorization.IsAllowed(user, RolePermission.ManageUsersAndRoles),
            ScreenRegistry.WorkflowConfiguration => authorization.IsAllowed(user, RolePermission.ManageGlobalConfiguration),
            ScreenRegistry.AuditLog => authorization.IsAllowed(user, RolePermission.ViewAuditLog),
            ScreenRegistry.Report => authorization.IsAllowed(user, RolePermission.ViewDimensionMatchResults),
            _ => false
        };
    }
    private static bool HasRole(AuthenticatedUser? user, string role) => user?.Roles.Any(x => string.Equals(x, role, StringComparison.OrdinalIgnoreCase)) == true;
}
