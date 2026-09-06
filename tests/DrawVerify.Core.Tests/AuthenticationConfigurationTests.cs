using DrawVerify.Core.Configuration;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class AuthenticationConfigurationTests
{
    [Fact(DisplayName = "TD-CORE-01 accepts the configured policy and empty MVP roles")]
    public void Valid_configuration_and_password_boundary_are_accepted()
    {
        var configuration = new AuthenticationConfiguration { PasswordMinimumLength = 8 };
        configuration.Validate();
        Assert.Null(configuration.ValidatePassword("Abcdef1!"));
        Assert.Empty(configuration.DefaultRoles);
        Assert.False(configuration.LockoutEnabled);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void Invalid_password_or_lockout_configuration_is_rejected(int minimum)
    {
        var configuration = new AuthenticationConfiguration { PasswordMinimumLength = minimum };
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Enabled_lockout_requires_positive_duration_and_policy_messages_are_specific()
    {
        Assert.Throws<InvalidOperationException>(() => new AuthenticationConfiguration { LockoutThreshold = 3, LockoutDuration = TimeSpan.Zero }.Validate());
        var configuration = new AuthenticationConfiguration { PasswordMinimumLength = 12 };
        Assert.Equal("Password must be at least 12 characters.", configuration.ValidatePassword("Ab1!"));
        Assert.Equal("Password must contain an uppercase letter.", configuration.ValidatePassword("abcdefghijkl1!"));
        Assert.Equal("Password must contain a lowercase letter.", configuration.ValidatePassword("ABCDEFGHIJKL1!"));
        Assert.Equal("Password must contain a digit.", configuration.ValidatePassword("Abcdefghijkl!"));
        Assert.Equal("Password must contain a non-alphanumeric character.", configuration.ValidatePassword("Abcdefghijkl1"));
    }

}
