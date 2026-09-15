using DrawVerify.Core.Models;

namespace DrawVerify.Core.Authorization;

public sealed class RoleAuthorization
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<RolePermission>> Permissions = new Dictionary<string, IReadOnlySet<RolePermission>>(StringComparer.Ordinal)
    {
        [SystemRoles.Admin] = new HashSet<RolePermission> { RolePermission.ManageUsersAndRoles, RolePermission.ViewAuditLog, RolePermission.ManageGlobalConfiguration },
        [SystemRoles.Maker] = new HashSet<RolePermission> { RolePermission.Upload, RolePermission.SubmitForReview, RolePermission.GenerateDxfDrawing, RolePermission.TriggerExtraction, RolePermission.TriggerDxfDimensionExtraction, RolePermission.ViewDxfDimensionResults, RolePermission.StartVerification, RolePermission.MatchDimensionsToGeometry, RolePermission.ViewDimensionMatchResults, RolePermission.ManageToleranceComparison, RolePermission.ViewMissingDimensions, RolePermission.CommentOnMissingDimensions, RolePermission.ViewExtraDimensions, RolePermission.ViewGdtCannotValidate },
        [SystemRoles.Checker] = new HashSet<RolePermission> { RolePermission.ReviewVerification, RolePermission.ViewPriorVersions, RolePermission.ResolveUnmatched, RolePermission.ApproveVerification, RolePermission.GenerateFinalReports, RolePermission.TriggerDxfDimensionExtraction, RolePermission.ViewDxfDimensionResults, RolePermission.ViewDimensionMatchResults, RolePermission.ManageToleranceComparison, RolePermission.ViewMissingDimensions, RolePermission.CommentOnMissingDimensions, RolePermission.ViewExtraDimensions, RolePermission.CommentOnExtraDimensions, RolePermission.FlagExtraDimensions, RolePermission.ViewGdtCannotValidate, RolePermission.ModifyGdtCannotValidate },
        [SystemRoles.Approver] = new HashSet<RolePermission> { RolePermission.ViewAuditLog, RolePermission.ViewPriorVersions, RolePermission.ReviewVerification, RolePermission.ResolveUnmatched, RolePermission.FinalApproval, RolePermission.ControlDocumentAccess, RolePermission.GenerateFinalReports, RolePermission.ViewDimensionMatchResults, RolePermission.ViewMissingDimensions, RolePermission.CommentOnMissingDimensions, RolePermission.ViewExtraDimensions, RolePermission.CommentOnExtraDimensions, RolePermission.FlagExtraDimensions, RolePermission.ViewGdtCannotValidate, RolePermission.ModifyGdtCannotValidate },
        [SystemRoles.Viewer] = new HashSet<RolePermission> { RolePermission.ReadApprovedDocuments, RolePermission.ReadApprovedReports, RolePermission.ViewMissingDimensions, RolePermission.ViewExtraDimensions, RolePermission.ViewGdtCannotValidate }
    };
    public bool IsAllowed(AuthenticatedUser? user, RolePermission permission)
    {
        if (user is null) return false;
        return user.Roles.Any(role => SystemRoles.TryCanonicalize(role, out var canonical) && Permissions.TryGetValue(canonical, out var allowed) && allowed.Contains(permission));
    }

    public IReadOnlySet<RolePermission> EffectivePermissions(IEnumerable<string>? roles)
    {
        var effective = new HashSet<RolePermission>();
        foreach (var role in roles ?? Array.Empty<string>())
        {
            if (SystemRoles.TryCanonicalize(role, out var canonical) && Permissions.TryGetValue(canonical, out var allowed))
                effective.UnionWith(allowed);
        }
        return effective;
    }
}
