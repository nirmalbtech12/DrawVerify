using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAuthenticationTests
{
    [Fact(DisplayName = "TSD-AC08 unauthenticated access requires authentication before retrieval")]
    public void Authentication_required_result_is_explicit()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.AuthenticationRequired, result.Failure);
        Assert.Contains("Authentication", result.Message, StringComparison.Ordinal);
    }
}
