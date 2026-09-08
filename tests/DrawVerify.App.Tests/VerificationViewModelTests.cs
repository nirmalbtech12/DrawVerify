using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Xunit;
using DrawVerify.Core.Enums;

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

    [Theory]
    [InlineData("Maker", true)]
    [InlineData("Checker", true)]
    [InlineData("Approver", false)]
    [InlineData("Viewer", false)]
    public void Matching_permission_is_limited_to_maker_and_checker(string role, bool expected)
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization(), null);
        Assert.False(vm.CanMatchDimensions);
        Assert.False(vm.CanViewDimensionMatches);
        Assert.Equal(expected, new RoleAuthorization().IsAllowed(session.CurrentUser, RolePermission.MatchDimensionsToGeometry));
    }

    [Fact]
    public void Result_items_have_distinct_status_labels_and_reasons()
    {
        var version = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var items = new[]
        {
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Matched, Confidence = MatchConfidence.High, StepGeometryReference = "Edge:1" }),
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound" }),
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.MissingDimension })
        };
        Assert.Equal(new[] { "Matched", "Unmatched", "Missing Dimension" }, items.Select(x => x.StatusLabel));
        Assert.Equal("Edge:1", items[0].GeometryReference);
        Assert.Equal("NoCorrespondingGeometryFound", items[1].Reason);
    }
}
