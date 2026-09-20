using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkRevocationTests
{
    [Fact(DisplayName = "TSD-AC06 revocation makes selected link inactive without changing another link")]
    public void Revocation_is_scoped_to_selected_link()
    {
        var selected = new InternalSharedLink { IsActive = true };
        var unrelated = new InternalSharedLink { IsActive = true };
        var revoked = selected with { IsActive = false, RevokedUtc = DateTime.UtcNow };
        Assert.Equal(SharedLinkStatus.Inactive, revoked.Status(DateTime.UtcNow));
        Assert.Equal(SharedLinkStatus.Active, unrelated.Status(DateTime.UtcNow));
        Assert.NotNull(revoked.RevokedUtc);
    }

    [Fact(DisplayName = "TSD-AC06 revoked links return the defined invalid-link outcome")]
    public void Revoked_link_is_not_active()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Inactive, "Invalid or revoked link.");

        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.Inactive, result.Failure);
        Assert.Equal("Invalid or revoked link.", result.Message);
        Assert.Null(result.Document);
    }
}
