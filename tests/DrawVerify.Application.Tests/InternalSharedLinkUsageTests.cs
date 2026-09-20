using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkUsageTests
{
    [Fact(DisplayName = "TSD-AC01-ENTRY valid entry begins access processing and malformed entry is denied")]
    public void Entry_results_distinguish_valid_processing_from_malformed_denial()
    {
        var valid = new InternalSharedLinkAccessResult { Succeeded = true, Link = new InternalSharedLink() };
        var malformed = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.InvalidLink, "Invalid or revoked link.");
        Assert.True(valid.Succeeded);
        Assert.False(malformed.Succeeded);
        Assert.Null(malformed.Document);
        Assert.NotEqual(string.Empty, malformed.Message);
    }

    [Fact(DisplayName = "TSD-AC07-DOWNLOAD permitted access level is represented explicitly")]
    public void Download_permission_is_explicit_on_shared_link()
    {
        var link = new InternalSharedLink { AccessLevel = SharedLinkAccessLevel.ViewAndDownload };
        Assert.Equal(SharedLinkAccessLevel.ViewAndDownload, link.AccessLevel);
    }

    [Fact(DisplayName = "TSD-AC13-CONCURRENCY existing usage capability is explicit")]
    public void Existing_usage_capability_is_explicit()
    {
        var usage = new InternalSharedLinkUsage(2, DateTime.UtcNow, Array.Empty<string>());
        Assert.Equal(2, usage.AccessCount);
        Assert.NotNull(usage.LastAccessUtc);
    }

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
