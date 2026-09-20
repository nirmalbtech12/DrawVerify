using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkConfigurationTests
{
    [Fact(DisplayName = "TSD-AC03 configuration retains permission activation and UTC expiry")]
    public void Valid_configuration_is_retained_and_invalid_expiry_fails()
    {
        var expiry = DateTime.UtcNow.AddHours(1);
        var configuration = new InternalSharedLinkConfiguration { AccessLevel = SharedLinkAccessLevel.ViewAndDownload, IsActive = false, ExpiresUtc = expiry };
        configuration.Validate(DateTime.UtcNow);
        Assert.Equal(SharedLinkAccessLevel.ViewAndDownload, configuration.AccessLevel);
        Assert.False(configuration.IsActive);
        Assert.Equal(expiry, configuration.ExpiresUtc);
        Assert.Throws<ArgumentException>(() => (configuration with { ExpiresUtc = DateTime.Now }).Validate());
        Assert.Throws<ArgumentException>(() => (configuration with { ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) }).Validate());
    }

    [Fact(DisplayName = "TSD-AC03 policy rejects disallowed download without downgrading")]
    public void Disallowed_download_is_not_silently_downgraded()
    {
        var policy = new InternalSharedLinkPolicy { AllowDownload = false };
        var configuration = new InternalSharedLinkConfiguration { AccessLevel = SharedLinkAccessLevel.ViewAndDownload };

        Assert.False(policy.IsAccessLevelAllowed(configuration.AccessLevel));
        Assert.Equal(SharedLinkAccessLevel.ViewAndDownload, configuration.AccessLevel);
    }
}
