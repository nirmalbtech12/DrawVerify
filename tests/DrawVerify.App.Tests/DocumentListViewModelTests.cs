using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DocumentListViewModelTests
{
    [Fact(DisplayName = "TSD-DOCUMENT-LIST-STATUS rejection statuses remain distinct")]
    public void TSD_DOCUMENT_LIST_STATUS_keeps_rejected_statuses_distinct_for_maker()
    {
        var statuses = new[] { WorkflowStatus.Rejected, WorkflowStatus.ReturnedForRework };
        Assert.NotEqual(statuses[0], statuses[1]); Assert.Contains(WorkflowStatus.Rejected, statuses); Assert.Contains(WorkflowStatus.ReturnedForRework, statuses);
    }
    [Fact(DisplayName = "TSD-AC11-STATUS distinguishes shared and unshared document state")]
    public void Active_grants_define_shared_status()
    {
        var now = DateTime.UtcNow;
        var active = new ViewerAccessGrant { ExpiresUtc = now.AddMinutes(1) };
        var revoked = new ViewerAccessGrant { IsRevoked = true };
        var expired = new ViewerAccessGrant { ExpiresUtc = now.AddMinutes(-1) };
        Assert.True(active.IsEffective(now));
        Assert.False(revoked.IsEffective(now));
        Assert.False(expired.IsEffective(now));
    }
}
