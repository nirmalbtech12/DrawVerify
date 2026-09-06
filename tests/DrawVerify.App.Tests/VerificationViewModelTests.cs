using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class VerificationViewModelTests
{
    [Theory(DisplayName = "TD-APPUI-002 review guard follows Checker-or-Approver authorization")]
    [InlineData("Checker", true)]
    [InlineData("Approver", true)]
    [InlineData("Viewer", false)]
    [InlineData("Maker", false)]
    public void Review_guard_returns_behavioral_result(string role, bool expected)
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization());
        Assert.Equal(expected, vm.CanReview); Assert.Equal(expected, vm.CanEnterReview);
        var entered = vm.TryEnterReview(out var message); Assert.Equal(expected, entered);
        if (expected) Assert.Empty(message); else Assert.Contains("Checker or Approver", message);
    }
}
