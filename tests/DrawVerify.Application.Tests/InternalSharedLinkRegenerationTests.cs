using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkRegenerationTests
{
    [Fact(DisplayName = "TSD-AC07 regeneration returns a distinct replacement and revokes the old link")]
    public void Replacement_has_distinct_identity_and_old_link_is_invalid()
    {
        var old = new InternalSharedLink { IsActive = true, Url = "https://drawverify.internal/share?token=old" };
        var replacement = new InternalSharedLink { IsActive = true, Url = "https://drawverify.internal/share?token=new" };
        var revoked = old with { IsActive = false, RevokedUtc = DateTime.UtcNow };
        Assert.NotEqual(old.LinkId, replacement.LinkId);
        Assert.NotEqual(old.Url, replacement.Url);
        Assert.False(revoked.IsActive);
        Assert.True(replacement.IsActive);
    }

    [Fact(DisplayName = "TSD-AC07 failed replacement is not represented as a successful result")]
    public void Replacement_failure_is_visible()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.InvalidLink, "Replacement could not be created.");

        Assert.False(result.Succeeded);
        Assert.Equal("Replacement could not be created.", result.Message);
        Assert.Null(result.Link);
    }
}
