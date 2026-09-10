using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Windows.Media;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DimensionMatchItemViewModelTests
{
    [Fact(DisplayName = "TD-06 exposes unmatched metadata, comments, and resolution state")]
    public void Unmatched_item_exposes_review_metadata()
    {
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 14, NominalValue = 30, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .2, ToleranceUnit = "mm", DxfLayer = "DIM", PreviewAnchor = "10,20", DimensionText = "30", ReasonCode = "NoCorrespondingGeometryFound", Review = new DimensionReview { DimensionMatchId = Guid.NewGuid(), Comment = "Expected missing", Status = DimensionResolutionStatus.Open } });
        Assert.True(item.IsUnmatched); Assert.Equal("DIM", item.Layer); Assert.Equal("10,20", item.PreviewAnchor); Assert.Equal("Expected missing", item.Comment); Assert.Equal("Open", item.ResolutionStatus); Assert.Equal("30", item.NominalValue);
    }
    [Fact(DisplayName = "TD-APP-001 maps canonical statuses to labels and distinct visual metadata")]
    public void Canonical_statuses_have_consistent_labels_and_colors()
    {
        var matched = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Matched });
        var mismatched = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched });
        var unmatched = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched });
        var legacy = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.MissingDimension });

        Assert.Equal("Matched", matched.StatusLabel);
        Assert.Equal("Green", matched.StatusColor);
        Assert.Equal(Brushes.Green, matched.StatusBrush);
        Assert.Equal("Mismatched", mismatched.StatusLabel);
        Assert.Equal("Red", mismatched.StatusColor);
        Assert.Equal(Brushes.Red, mismatched.StatusBrush);
        Assert.Equal("Unmatched", unmatched.StatusLabel);
        Assert.Contains(unmatched.StatusColor, new[] { "DarkOrange", "Gray" });
        Assert.Contains(unmatched.StatusBrush, new[] { Brushes.DarkOrange, Brushes.Gray });
        Assert.Equal("Missing Dimension", legacy.StatusLabel);
        Assert.Equal("Gray", legacy.StatusColor);
        Assert.NotEqual("Matched", legacy.StatusLabel);
    }

    [Fact(DisplayName = "TD-UI-001 renders extra dimension properties and unavailable location")]
    public void Extra_item_exposes_feature_value_reference_tolerance_and_location()
    {
        var complete = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, ReasonCode = "NoCorrespondingDimension", StepGeometryReference = "Face:7:Cylinder", GeometryType = "Hole", StepFeatureValue = 12.5, StepLocationX = 1.25, StepLocationY = 2.5, StepLocationZ = 3.75, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .2, ToleranceUnit = "mm" });
        var unavailable = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Face:8" });

        Assert.True(complete.IsExtraDimension);
        Assert.Equal("Extra Dimension", complete.StatusLabel);
        Assert.Equal("Face:7:Cylinder", complete.GeometryReference);
        Assert.Equal("12.5", complete.FeatureValue);
        Assert.Equal("(1.25, 2.5, 3.75)", complete.FeatureLocation);
        Assert.Contains("mm", complete.Tolerance);
        Assert.Equal("Unavailable", unavailable.FeatureLocation);
    }
}
