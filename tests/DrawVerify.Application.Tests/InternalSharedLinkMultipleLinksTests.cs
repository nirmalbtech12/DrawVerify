using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkMultipleLinksTests
{
    [Fact(DisplayName = "TSD-AC14 multiple-link policy preserves independent link configuration")]
    public void Multiple_links_have_independent_settings()
    {
        var policy = new InternalSharedLinkPolicy { AllowMultipleLinks = true, AllowDownload = true };
        var first = new InternalSharedLinkConfiguration { AllowMultipleLinks = true, AccessLevel = SharedLinkAccessLevel.ViewOnly };
        var second = new InternalSharedLinkConfiguration { AllowMultipleLinks = true, AccessLevel = SharedLinkAccessLevel.ViewAndDownload };
        Assert.True(policy.AllowMultipleLinks);
        Assert.NotEqual(first.AccessLevel, second.AccessLevel);
        Assert.True(policy.IsAccessLevelAllowed(second.AccessLevel));
    }
}
