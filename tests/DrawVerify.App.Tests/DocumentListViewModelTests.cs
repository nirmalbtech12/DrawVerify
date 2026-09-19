using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DocumentListViewModelTests
{
    [Fact(DisplayName = "TSD-AC11-STATUS distinguishes shared and unshared document state")]
    public void Active_grants_define_shared_status()
    {
        var now = DateTime.UtcNow;
        var active = new ViewerAccessGrant { ExpiresUtc = now.AddMinutes(1) };
        var revoked = new ViewerAccessGrant { IsRevoked = true };
        var expired = new ViewerAccessGrant { ExpiresUtc = now.AddMinutes(-1) };
        Assert.True(active.IsEffective(now));
        Assert.False(revoked.IsEffective(now));
        Assert.False(expired.IsEffective(now));
    }
}
