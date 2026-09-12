using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class RoleAuthorizationTests
{
    [Fact(DisplayName = "TD-CORE-001 catalog exposes exactly the assignable workflow roles")]
    public void Workflow_catalog_is_exactly_maker_checker_approver()
    {
        Assert.Equal(new[] { SystemRoles.Maker, SystemRoles.Checker, SystemRoles.Approver }, SystemRoles.WorkflowRoles.Select(x => x.Id));
        Assert.DoesNotContain(SystemRoles.Admin, SystemRoles.WorkflowRoles.Select(x => x.Id));
        Assert.DoesNotContain(SystemRoles.Viewer, SystemRoles.WorkflowRoles.Select(x => x.Id));
    }

    [Fact(DisplayName = "TD-CORE-001 validates the immutable five-role catalog and permission union")]
    public void Catalog_has_exactly_five_roles_and_permissions_are_centralized()
    {
        var catalog = SystemRoles.Catalog;
        Assert.Equal(new[] { "Admin", "Maker", "Checker", "Approver", "Viewer" }, catalog.Select(x => x.Id));
        Assert.Equal(5, catalog.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog, role => { Assert.False(string.IsNullOrWhiteSpace(role.Name)); Assert.False(string.IsNullOrWhiteSpace(role.Description)); });

        var policy = new RoleAuthorization();
        Assert.True(policy.IsAllowed(new AuthenticatedUser("maker", new[] { "mAkEr", "Viewer" }), RolePermission.Upload));
        Assert.True(policy.IsAllowed(new AuthenticatedUser("mixed", new[] { "Viewer", "Checker" }), RolePermission.ReviewVerification));
        Assert.False(policy.IsAllowed(new AuthenticatedUser("viewer", new[] { "Viewer" }), RolePermission.Upload));
        Assert.False(policy.IsAllowed(null, RolePermission.Upload));
        Assert.False(policy.IsAllowed(new AuthenticatedUser("none", Array.Empty<string>()), RolePermission.Upload));

        Assert.Throws<NotSupportedException>(() => ((IList<RoleDefinition>)catalog).Add(catalog[0]));
    }

    [Theory(DisplayName = "TD-CORE-001 rejects invalid role assignments")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void Invalid_roles_are_rejected(string role) => Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalize(new[] { role }));

    [Fact(DisplayName = "TD-CORE-001 canonicalizes role names before applying permissions")]
    public void Duplicate_roles_are_rejected_and_known_casing_is_canonicalized()
    {
        Assert.Equal(new[] { "Maker", "Viewer" }, SystemRoles.ValidateAndCanonicalize(new[] { " maker ", "VIEWER" }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalize(new[] { "Admin", "admin" }));
    }

    [Theory(DisplayName = "TD-CORE-002 workflow role validation canonicalizes and rejects invalid input")]
    [InlineData(" maker ", "Maker")]
    [InlineData("CHECKER", "Checker")]
    [InlineData("Approver", "Approver")]
    public void Workflow_role_validation_canonicalizes_supported_values(string input, string expected)
        => Assert.Equal(new[] { expected }, SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { input }));

    [Fact(DisplayName = "TD-CORE-002 workflow role validation rejects duplicates, unknown, null, and out-of-scope roles")]
    public void Workflow_role_validation_rejects_invalid_values()
    {
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { "Maker", "maker" }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { "Unknown" }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { (string)null! }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { SystemRoles.Admin }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalizeWorkflowRoles(new[] { SystemRoles.Viewer }));
    }

    [Fact(DisplayName = "TD-CORE-003 effective permissions are the union of assigned roles")]
    public void Effective_permissions_are_union_without_admin_privilege()
    {
        var authorization = new RoleAuthorization();
        var maker = authorization.EffectivePermissions(new[] { SystemRoles.Maker });
        var checker = authorization.EffectivePermissions(new[] { SystemRoles.Checker });
        var combined = authorization.EffectivePermissions(new[] { SystemRoles.Maker, SystemRoles.Checker });
        Assert.Contains(RolePermission.Upload, maker);
        Assert.Contains(RolePermission.ReviewVerification, checker);
        Assert.All(maker.Union(checker), permission => Assert.Contains(permission, combined));
        Assert.DoesNotContain(RolePermission.ManageUsersAndRoles, combined);
        Assert.Equal(combined, authorization.EffectivePermissions(new[] { SystemRoles.Maker, SystemRoles.Checker, SystemRoles.Maker }));
    }

    [Theory(DisplayName = "TD-CORE-002 applies Cannot Validate review permissions by role")]
    [InlineData("Checker", true, true)]
    [InlineData("Approver", true, true)]
    [InlineData("Viewer", true, false)]
    [InlineData("Maker", true, false)]
    public void Cannot_validate_review_permission_matrix_is_explicit(string role, bool canView, bool canModify)
    {
        var user = new AuthenticatedUser(role.ToLowerInvariant(), new[] { role });
        var authorization = new RoleAuthorization();

        Assert.Equal(canView, authorization.IsAllowed(user, RolePermission.ViewGdtCannotValidate));
        Assert.Equal(canModify, authorization.IsAllowed(user, RolePermission.ModifyGdtCannotValidate));
        Assert.False(authorization.IsAllowed(null, RolePermission.ViewGdtCannotValidate));
    }
}
