using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkReadOnlyViewerTests
{
    [Theory(DisplayName = "TSD-AC05-VIEW shared-link viewer is read-only for supported content"), InlineData(true), InlineData(false)]
    public void Shared_link_context_is_read_only_for_any_download_policy(bool canDownload)
    {
        var context = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), canDownload);
        Assert.True(context.IsReadOnly);
        Assert.False(context.CanEdit);
        Assert.False(context.CanWorkflow);
        Assert.False(context.CanReshare);
    }

    [Fact(DisplayName = "TSD-AC07-DOWNLOAD permitted viewer policy exposes download capability")]
    public void Permitted_context_exposes_download_capability()
    {
        var context = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        Assert.True(context.CanDownload);
    }

    [Fact(DisplayName = "TSD-AC08-NODOWNLOAD disabled viewer policy does not expose download capability")]
    public void Disabled_context_does_not_expose_download_capability()
    {
        var context = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);
        Assert.False(context.CanDownload);
        Assert.True(context.IsReadOnly);
    }

    [Fact(DisplayName = "TSD-AC14-CONSISTENCY supported contexts preserve the same read-only policy")]
    public void Equivalent_supported_contexts_preserve_read_only_policy()
    {
        var first = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);
        var second = new SharedLinkViewerContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);
        Assert.Equal(first.IsReadOnly, second.IsReadOnly);
        Assert.Equal(first.CanDownload, second.CanDownload);
        Assert.Equal(first.CanEdit, second.CanEdit);
        Assert.Equal(first.CanWorkflow, second.CanWorkflow);
    }

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
