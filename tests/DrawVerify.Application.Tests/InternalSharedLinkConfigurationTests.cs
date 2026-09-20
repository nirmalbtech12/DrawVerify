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
    }
}
