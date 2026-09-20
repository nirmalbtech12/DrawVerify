using DrawVerify.App.ViewModels;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedLinkErrorViewModelTests
{
    [Theory(DisplayName = "TSD-AC09-ERROR link failures remain clear"), InlineData("This link has expired."), InlineData("Invalid or revoked link.")]
    public void Link_failure_message_is_visible(string message)
    {
        var model = new AccessDeniedViewModel(message);
        Assert.Equal(message, model.Message);
        Assert.NotEqual(string.Empty, model.Message);
    }

    [Fact(DisplayName = "TSD-AC10-AUTHERROR unauthorized access presents a permission error")]
    public void Unauthorized_message_is_visible()
    {
        var model = new AccessDeniedViewModel("You are not authorized to access this shared link.");
        Assert.Contains("authorized", model.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TSD-AC15 invalid shared-link errors remain explicit")]
    public void Error_view_model_preserves_specific_message()
    {
        var model = new AccessDeniedViewModel("This link has expired.");
        Assert.Equal("This link has expired.", model.Message);
    }
}
