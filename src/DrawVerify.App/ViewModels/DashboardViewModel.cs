namespace DrawVerify.App.ViewModels;

using System.Collections.ObjectModel;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;

public sealed class DashboardViewModel
{
    public sealed record UsageGuideSection(string RoleName, string RoleDescription, IReadOnlyList<string> Steps);
    public ObservableCollection<UsageGuideSection> UsageGuide { get; } = new();
    public bool HasUsageGuide => UsageGuide.Count > 0;
    public bool HasNoUsageGuide => !HasUsageGuide;

    public DashboardViewModel(ISessionAuthenticationService session, RoleAuthorization authorization)
    {
        var user = session.CurrentUser;
        if (user is null) return;

        foreach (var role in SystemRoles.Catalog.Where(role => user.Roles.Any(value => SystemRoles.TryCanonicalize(value, out var canonical) && canonical == role.Id)))
        {
            var steps = StepsFor(role.Id, authorization, user);
            if (steps.Count > 0) UsageGuide.Add(new(role.Name, role.Description, steps));
        }
    }

    private static IReadOnlyList<string> StepsFor(string role, RoleAuthorization authorization, DrawVerify.Core.Models.AuthenticatedUser user) => role switch
    {
        SystemRoles.Admin => new[]
        {
            "Manage users and assign roles when user administration is available.",
            "Review the audit log to investigate authentication and administrative activity.",
            "Maintain global application configuration."
        }.Where((_, index) => index switch
        {
            0 => authorization.IsAllowed(user, RolePermission.ManageUsersAndRoles),
            1 => authorization.IsAllowed(user, RolePermission.ViewAuditLog),
            _ => authorization.IsAllowed(user, RolePermission.ManageGlobalConfiguration)
        }).ToArray(),
        SystemRoles.Maker => new[]
        {
            "Upload the STEP model and DXF drawing from the Combined Upload & Verify screen.",
            "Run STEP geometry and DXF dimension extraction, then confirm both files loaded successfully.",
            "Start verification and review dimension matches, missing dimensions, extra dimensions, and GD&T items.",
            "Adjust the comparison tolerance when the engineering process requires it."
        }.Where((_, index) => index switch
        {
            0 => authorization.IsAllowed(user, RolePermission.Upload),
            1 => authorization.IsAllowed(user, RolePermission.TriggerExtraction) && authorization.IsAllowed(user, RolePermission.TriggerDxfDimensionExtraction),
            2 => authorization.IsAllowed(user, RolePermission.StartVerification) && authorization.IsAllowed(user, RolePermission.ViewDimensionMatchResults),
            _ => authorization.IsAllowed(user, RolePermission.ManageToleranceComparison)
        }).ToArray(),
        SystemRoles.Checker => new[]
        {
            "Review the verification results and investigate mismatched or unmatched dimensions.",
            "Resolve unmatched dimensions and record comments for missing or extra dimensions.",
            "Review GD&T items that require manual validation and update their disposition.",
            "Approve verification results or generate the final report when the review is complete."
        }.Where((_, index) => index switch
        {
            0 => authorization.IsAllowed(user, RolePermission.ReviewVerification),
            1 => authorization.IsAllowed(user, RolePermission.ResolveUnmatched) && authorization.IsAllowed(user, RolePermission.CommentOnMissingDimensions),
            2 => authorization.IsAllowed(user, RolePermission.ModifyGdtCannotValidate),
            _ => authorization.IsAllowed(user, RolePermission.ApproveVerification) || authorization.IsAllowed(user, RolePermission.GenerateFinalReports)
        }).ToArray(),
        SystemRoles.Approver => new[]
        {
            "Review the completed verification results and unresolved findings.",
            "Perform final approval after confirming the checker review is complete.",
            "Control document access and generate final verification reports."
        }.Where((_, index) => index switch
        {
            0 => authorization.IsAllowed(user, RolePermission.ReviewVerification),
            1 => authorization.IsAllowed(user, RolePermission.FinalApproval),
            _ => authorization.IsAllowed(user, RolePermission.ControlDocumentAccess) || authorization.IsAllowed(user, RolePermission.GenerateFinalReports)
        }).ToArray(),
        SystemRoles.Viewer => new[]
        {
            "Open approved documents and reports in read-only mode.",
            "Review missing dimensions, extra dimensions, and GD&T items marked for review."
        }.Where((_, index) => index == 0
            ? authorization.IsAllowed(user, RolePermission.ReadApprovedDocuments) || authorization.IsAllowed(user, RolePermission.ReadApprovedReports)
            : authorization.IsAllowed(user, RolePermission.ViewMissingDimensions) || authorization.IsAllowed(user, RolePermission.ViewExtraDimensions) || authorization.IsAllowed(user, RolePermission.ViewGdtCannotValidate)).ToArray(),
        _ => Array.Empty<string>()
    };
}
