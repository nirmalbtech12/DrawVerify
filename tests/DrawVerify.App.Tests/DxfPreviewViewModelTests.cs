using DrawVerify.App.ViewModels;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DxfPreviewViewModelTests
{
    [Fact(DisplayName = "T19 DXF selection highlights and centers valid anchors")]
    public void Valid_anchor_is_highlighted_and_centered()
    {
        var viewModel = new DxfPreviewViewModel();
        Assert.True(viewModel.SelectDimension(7, "entity:7", 12.5, 8.25));
        Assert.True(viewModel.IsHighlightVisible);
        Assert.Equal("12.5,8.25", viewModel.NavigationTarget);
        Assert.Contains("highlighted and centered", viewModel.PreviewStatus);
    }

    [Fact(DisplayName = "T19 invalid DXF anchor remains unavailable")]
    public void Invalid_anchor_does_not_claim_highlight_success()
    {
        var viewModel = new DxfPreviewViewModel();
        Assert.False(viewModel.SelectDimension(0, null));
        Assert.False(viewModel.IsHighlightVisible);
        Assert.Contains("unavailable", viewModel.PreviewStatus, StringComparison.OrdinalIgnoreCase);
    }
}
