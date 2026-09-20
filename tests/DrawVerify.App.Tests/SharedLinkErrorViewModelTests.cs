using DrawVerify.App.ViewModels;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkErrorViewModelTests
{
    [Fact(DisplayName = "TSD-AC15 invalid shared-link errors remain explicit")]
    public void Error_view_model_preserves_specific_message()
    {
        var model = new AccessDeniedViewModel("This link has expired.");
        Assert.Equal("This link has expired.", model.Message);
    }
}
