using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkInvalidLinkTests
{
    [Theory(DisplayName = "TSD-AC03-VALIDATE link status validation denies failed conditions"), InlineData(SharedLinkStatus.Inactive), InlineData(SharedLinkStatus.Expired)]
    public void Failed_link_status_is_not_authorized(SharedLinkStatus status)
    {
        var link = new InternalSharedLink { IsActive = status != SharedLinkStatus.Inactive, ExpiresUtc = status == SharedLinkStatus.Expired ? DateTime.UtcNow.AddMinutes(-1) : null };
        Assert.Equal(status, link.Status(DateTime.UtcNow));
        Assert.NotEqual(SharedLinkStatus.Active, link.Status(DateTime.UtcNow));
    }

    [Fact(DisplayName = "TSD-AC09-ERROR revoked and expired states produce clear denial results")]
    public void Revoked_and_expired_results_are_clear()
    {
        var revoked = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Inactive, "Invalid or revoked link.");
        var expired = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Expired, "This link has expired.");
        Assert.All(new[] { revoked, expired }, result => { Assert.False(result.Succeeded); Assert.NotEqual(string.Empty, result.Message); Assert.Null(result.Document); });
    }

    [Theory(DisplayName = "TSD-AC15 invalid links deny access with clear category"), InlineData(InternalSharedLinkAccessFailure.InvalidLink, "Invalid or revoked link."), InlineData(InternalSharedLinkAccessFailure.Expired, "This link has expired."), InlineData(InternalSharedLinkAccessFailure.Inactive, "Invalid or revoked link."), InlineData(InternalSharedLinkAccessFailure.Unauthorized, "You are not authorized to access this shared link.")]
    public void Invalid_link_result_is_not_successful(InternalSharedLinkAccessFailure failure, string message)
    {
        var result = InternalSharedLinkAccessResult.Fail(failure, message);
        Assert.False(result.Succeeded);
        Assert.Equal(message, result.Message);
        Assert.Null(result.Document);
    }
}
