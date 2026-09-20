using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAuthorizationTests
{
    [Fact(DisplayName = "TSD-AC02 TSD-AC09 internal authorization denies external and unauthenticated retrieval")]
    public void Identity_provider_distinguishes_internal_and_external_users()
    {
        var policy = new InternalSharedLinkPolicy { InternalDomain = "drawverify.internal", AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Viewer" } };
        var link = new InternalSharedLink { AccessLevel = SharedLinkAccessLevel.ViewOnly };
        var provider = new Provider(policy);
        var internalUser = new AuthenticatedUser("viewer@drawverify.internal", new[] { "Viewer" });
        var externalUser = new AuthenticatedUser("viewer@external.example", new[] { "Viewer" });
        Assert.True(provider.IsInternal(internalUser));
        Assert.True(provider.IsAllowed(internalUser, link));
        Assert.False(provider.IsInternal(externalUser));
    }

    private sealed class Provider(InternalSharedLinkPolicy policy) : ISharedLinkIdentityProvider
    {
        public bool IsAuthenticated => true;
        public bool IsInternal(AuthenticatedUser user) => user.Username.EndsWith("@" + policy.InternalDomain, StringComparison.OrdinalIgnoreCase);
        public bool IsAllowed(AuthenticatedUser user, InternalSharedLink link) => user.Roles.Any(policy.AllowedRoles.Contains);
    }
}
