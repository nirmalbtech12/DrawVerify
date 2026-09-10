using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class RoleAuthorizationTests
{
    [Theory(DisplayName = "TD-CORE-002 extra-dimension permission matrix is explicit")]
    [InlineData("Checker", true, true, true)]
    [InlineData("Approver", true, true, true)]
    [InlineData("Viewer", true, false, false)]
    [InlineData("Maker", true, false, false)]
    public void Extra_permissions_follow_role_policy(string role, bool canView, bool canComment, bool canResolve)
    {
        var authorization = new RoleAuthorization(); var user = new AuthenticatedUser("user", new[] { role });
        Assert.Equal(canView, authorization.IsAllowed(user, RolePermission.ViewExtraDimensions));
        Assert.Equal(canComment, authorization.IsAllowed(user, RolePermission.CommentOnExtraDimensions));
        Assert.Equal(canResolve, authorization.IsAllowed(user, RolePermission.ResolveExtraDimensions));
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

    [Fact]
    public void Duplicate_roles_are_rejected_and_known_casing_is_canonicalized()
    {
        Assert.Equal(new[] { "Maker", "Viewer" }, SystemRoles.ValidateAndCanonicalize(new[] { " maker ", "VIEWER" }));
        Assert.Throws<InvalidOperationException>(() => SystemRoles.ValidateAndCanonicalize(new[] { "Admin", "admin" }));
    }
}
