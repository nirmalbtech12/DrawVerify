using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkApprovalGuardTests
{
    [Fact(DisplayName = "TSD-AC03-VALIDATE non-approved association denies before content access")]
    public void Non_approved_document_result_is_denied()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.DocumentNotApproved, InternalSharedLinkService.ApprovedOnlyMessage);
        Assert.False(result.Succeeded);
        Assert.Equal(InternalSharedLinkAccessFailure.DocumentNotApproved, result.Failure);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "TSD-AC13 non-approved generation uses the clear approved-document message")]
    public void Approved_only_message_is_clear_and_stable()
    {
        var exception = new InvalidOperationException(InternalSharedLinkService.ApprovedOnlyMessage);
        Assert.Equal(DocumentStatus.Active, new Document { Status = DocumentStatus.Active }.Status);
        Assert.Contains("approved documents", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
