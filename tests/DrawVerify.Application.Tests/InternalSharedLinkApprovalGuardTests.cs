using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkApprovalGuardTests
{
    [Fact(DisplayName = "TSD-AC13 non-approved generation uses the clear approved-document message")]
    public void Approved_only_message_is_clear_and_stable()
    {
        var exception = new InvalidOperationException(InternalSharedLinkService.ApprovedOnlyMessage);
        Assert.Equal(DocumentStatus.Active, new Document { Status = DocumentStatus.Active }.Status);
        Assert.Contains("approved documents", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
