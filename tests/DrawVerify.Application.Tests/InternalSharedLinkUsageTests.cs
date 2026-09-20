using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkUsageTests
{
    [Fact(DisplayName = "TSD-AC12 usage exposes count last access and policy-filtered users")]
    public void Usage_values_are_explicit_when_tracking_is_enabled_or_unavailable()
    {
        var last = DateTime.UtcNow.AddMinutes(-2);
        var usage = new InternalSharedLinkUsage(3, last, new[] { "viewer@drawverify.internal" });
        Assert.Equal(3, usage.AccessCount);
        Assert.Equal(last, usage.LastAccessUtc);
        Assert.Single(usage.AccessingUsers);
        var unavailable = new InternalSharedLinkDetails { UsageUnavailable = true };
        Assert.True(unavailable.UsageUnavailable);
        Assert.Null(unavailable.AccessCount);
    }
}
