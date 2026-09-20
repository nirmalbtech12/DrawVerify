using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkInvalidLinkTests
{
    [Theory(DisplayName = "TSD-AC15 invalid links deny access with clear category"), InlineData(InternalSharedLinkAccessFailure.InvalidLink, "Invalid or revoked link."), InlineData(InternalSharedLinkAccessFailure.Expired, "This link has expired.")]
    public void Invalid_link_result_is_not_successful(InternalSharedLinkAccessFailure failure, string message)
    {
        var result = InternalSharedLinkAccessResult.Fail(failure, message);
        Assert.False(result.Succeeded);
        Assert.Equal(message, result.Message);
        Assert.Null(result.Document);
    }
}
