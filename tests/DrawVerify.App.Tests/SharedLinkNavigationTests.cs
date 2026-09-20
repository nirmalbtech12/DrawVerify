using DrawVerify.Core.Models;
using DrawVerify.App;
using System.Reflection;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkNavigationTests
{
    [Theory(DisplayName = "TSD-AC01-ENTRY route parsing accepts supported token routes and rejects malformed routes"), InlineData("https://drawverify.internal/share?token=abc", "abc"), InlineData("not-a-route", null)]
    public void Route_parsing_preserves_supported_token_or_rejects_malformed(string route, string? expected)
    {
        var method = typeof(MainWindow).GetMethod("ExtractSharedLinkToken", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = (string?)method.Invoke(null, new object[] { route });
        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "TSD-AC12-DEEPLINK malformed optional parameters retain safe route token")]
    public void Deep_link_parameters_do_not_replace_the_validated_token()
    {
        var method = typeof(MainWindow).GetMethod("ExtractSharedLinkToken", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = (string?)method.Invoke(null, new object[] { "https://drawverify.internal/share?token=abc&page=not-a-number" });
        Assert.Equal("abc", actual);
    }

    [Fact(DisplayName = "TSD-AC08 shared-link access represents authentication-required handoff")]
    public void Authentication_required_state_is_not_successful_navigation()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
    }
}
