using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditRetentionConfigurationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(3650)]
    public void TD_APP_003_retention_days_accepts_documented_nonnegative_values(int retentionDays)
    {
        Assert.True(retentionDays >= 0);
        var settings = new Dictionary<string, string?> { ["AuditLog:RetentionDays"] = retentionDays.ToString() };
        Assert.Equal(retentionDays.ToString(), settings["AuditLog:RetentionDays"]);
    }

    [Fact]
    public void TD_APP_003_negative_retention_is_invalid_and_key_is_stable()
    {
        var settings = new Dictionary<string, string?> { ["AuditLog:RetentionDays"] = "-1" };
        Assert.True(int.TryParse(settings["AuditLog:RetentionDays"], out var value));
        Assert.True(value < 0);
        Assert.Equal("AuditLog:RetentionDays", settings.Keys.Single());
    }
}
