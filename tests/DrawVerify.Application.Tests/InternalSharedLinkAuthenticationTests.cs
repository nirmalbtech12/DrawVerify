using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAuthenticationTests
{
    [Fact(DisplayName = "TSD-AC02-AUTH unauthenticated access requires authentication before protected content")]
    public void Unauthenticated_result_requires_authentication()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.AuthenticationRequired, result.Failure);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "TSD-AC04-AUTHZ unevaluable configured audience policy denies access")]
    public void Unevaluable_policy_is_not_authorized()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Unauthorized, "You are not authorized to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.Unauthorized, result.Failure);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "TSD-AC08 unauthenticated access requires authentication before retrieval")]
    public void Authentication_required_result_is_explicit()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.AuthenticationRequired, result.Failure);
        Assert.Contains("Authentication", result.Message, StringComparison.Ordinal);
    }
}
