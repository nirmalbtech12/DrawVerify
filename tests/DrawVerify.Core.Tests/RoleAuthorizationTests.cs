using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class RoleAuthorizationTests
{
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
