using System.Reflection;
using DrawVerify.App.ViewModels;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class VerificationViewModelTests
{
    [Fact(DisplayName = "TD-APPUI-002 exposes a centralized verification-review authorization guard")]
    public void Verification_view_model_has_review_entry_guard()
    {
        var type = typeof(VerificationViewModel);
        var guard = type.GetProperty("CanReview") ?? type.GetProperty("CanEnterReview");
        Assert.NotNull(guard);
        Assert.Equal(typeof(bool), guard!.PropertyType);
        Assert.NotNull(type.GetMethod("TryEnterReview") ?? type.GetMethod("EnterReview"));
    }
}
