using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkNavigationTests
{
    [Fact(DisplayName = "TSD-AC08 shared-link access represents authentication-required handoff")]
    public void Authentication_required_state_is_not_successful_navigation()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
    }
}
