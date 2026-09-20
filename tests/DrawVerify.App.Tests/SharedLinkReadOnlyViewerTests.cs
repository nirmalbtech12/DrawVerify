using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkReadOnlyViewerTests
{
    [Fact(DisplayName = "TSD-AC10 shared-link viewer disables mutation and honors download permission")]
    public void Shared_link_context_is_read_only()
    {
        var context = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CanDownload: true);
        Assert.True(context.IsReadOnly);
        Assert.False(context.CanEdit);
        Assert.False(context.CanWorkflow);
        Assert.False(context.CanReshare);
        Assert.True(context.CanDownload);
    }
}
