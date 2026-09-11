namespace DrawVerify.App.ViewModels;

using System.Collections.ObjectModel;
using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;

public sealed class DashboardViewModel
{
    public sealed record QuickLink(string Label, string Target);
    public ObservableCollection<QuickLink> QuickLinks { get; } = new();
    public DashboardViewModel(ISessionAuthenticationService session, RoleAuthorization authorization)
    {
        var user = session.CurrentUser;
        Add("Upload model", ScreenRegistry.StepUpload, authorization.IsAllowed(user, RolePermission.Upload));
        Add("Verification", ScreenRegistry.Verification, authorization.IsAllowed(user, RolePermission.ReviewVerification) || authorization.IsAllowed(user, RolePermission.StartVerification) || authorization.IsAllowed(user, RolePermission.ReadApprovedDocuments));
        Add("Workflow", ScreenRegistry.Workflow, user?.Roles.Any(x => x is SystemRoles.Maker or SystemRoles.Checker or SystemRoles.Approver) == true);
        Add("Administration", ScreenRegistry.UserAdministration, authorization.IsAllowed(user, RolePermission.ManageUsersAndRoles));
    }
    private void Add(string label, string target, bool allowed) { if (allowed) QuickLinks.Add(new(label, target)); }
}
