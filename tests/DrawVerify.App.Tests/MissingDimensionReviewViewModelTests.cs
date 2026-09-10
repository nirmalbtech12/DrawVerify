using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class MissingDimensionReviewViewModelTests
{
    [Theory(DisplayName = "TD-UI-003 read-only roles cannot mutate extra review state")]
    [InlineData("Checker", true)] [InlineData("Approver", true)] [InlineData("Viewer", false)] [InlineData("Maker", false)]
    public void Extra_review_permission_matrix_is_read_only_for_viewer_and_maker(string role, bool canMutate)
    {
        var authorization = new DrawVerify.Core.Authorization.RoleAuthorization();
        var user = new DrawVerify.Core.Models.AuthenticatedUser("user", new[] { role });
        Assert.Equal(canMutate, authorization.IsAllowed(user, DrawVerify.Core.Authorization.RolePermission.CommentOnExtraDimensions));
        Assert.Equal(canMutate, authorization.IsAllowed(user, DrawVerify.Core.Authorization.RolePermission.ResolveExtraDimensions));
    }

    [Fact(DisplayName = "TD-006 and TD-007 expose location details and DXF navigation")]
    public void Item_and_preview_preserve_location_and_unavailable_anchor_state()
    {
        var item = new DimensionMatchItemViewModel(new DimensionMatch { DimensionMatchId = Guid.NewGuid(), DxfDimensionReference = 7, Status = DimensionMatchStatus.Unmatched, NominalValue = 50, DimensionText = "50.00", DxfLayer = "DIM", BlockName = "B1", ViewName = "Front", LocationX = 12.5, LocationY = 8 });
        Assert.Equal("DIM", item.Layer); Assert.Equal("B1", item.BlockName); Assert.Equal("Front", item.ViewName); Assert.Equal("(12.5, 8)", item.Location); Assert.Equal("Missing from 3D (STEP)", item.MissingFromStepLabel);
        var preview = new DxfPreviewViewModel(); Assert.True(preview.SelectDimension(7, null, 12.5, 8)); Assert.True(preview.IsHighlightVisible); Assert.Equal("12.5,8", preview.NavigationTarget); Assert.False(new DxfPreviewViewModel().SelectDimension(8, null));
    }
}
