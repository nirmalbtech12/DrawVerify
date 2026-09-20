using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkDetailsTests
{
    [Fact(DisplayName = "TSD-AC04 details expose URL status expiry and tracking count")]
    public void Details_reflect_current_link_state()
    {
        var now = DateTime.UtcNow;
        var link = new InternalSharedLink { Url = "https://drawverify.internal/share?token=opaque", IsActive = true, ExpiresUtc = now.AddMinutes(5) };
        var details = new InternalSharedLinkDetails { Link = link, Status = link.Status(now), AccessCount = 4, LastAccessUtc = now.AddMinutes(-1) };
        Assert.Equal(link.Url, details.FullUrl);
        Assert.Equal(SharedLinkStatus.Active, details.Status);
        Assert.Equal(link.ExpiresUtc, details.ExpiresUtc);
        Assert.Equal(4, details.AccessCount);
        Assert.Null(new InternalSharedLinkDetails { Link = link, Status = details.Status }.AccessCount);

        Assert.Equal(SharedLinkStatus.Expired, (link with { ExpiresUtc = now.AddMinutes(-1) }).Status(now));
        Assert.Equal(SharedLinkStatus.Inactive, (link with { IsActive = false }).Status(now));
    }
}
