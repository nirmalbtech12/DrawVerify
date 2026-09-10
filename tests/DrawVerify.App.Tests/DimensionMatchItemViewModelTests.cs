using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Windows.Media;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DimensionMatchItemViewModelTests
{
    [Fact(DisplayName = "TD-08 displays unsupported GD&T details and unavailable values")]
    public void Unsupported_gdt_item_exposes_review_fields()
    {
        var result = new GdtResult
        {
            GdtResultId = Guid.NewGuid(), Identifier = "gdt-10", ControlType = GdtControlType.Unknown,
            Symbol = "◇", ToleranceValue = "0.3", Modifiers = "MMC", DatumReferences = new[] { "B" },
            SourceDocument = "drawing.dxf", SourceLocation = "entity:10", Status = VerificationStatus.NotVerified,
            Reason = GdtResult.UnsupportedReason, ReviewComment = "Check"
        };
        var item = new GdtResultItemViewModel(result);

        Assert.Equal("Not Verified", item.StatusLabel);
        Assert.Equal("◇", item.ControlTypeOrSymbol);
        Assert.Equal("0.3", item.Tolerance);
        Assert.Equal("MMC", item.Modifiers);
        Assert.Equal("B", item.Datums);
        Assert.Equal("drawing.dxf @ entity:10", item.Source);
        Assert.Equal(GdtResult.UnsupportedReason, item.Reason);
        Assert.Equal("Check", item.Comment);
    }

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
        Assert.Equal("Extra Dimension (legacy)", legacy.StatusLabel);
        Assert.Equal("DarkOrange", legacy.StatusColor);
        Assert.NotEqual("Matched", legacy.StatusLabel);
    }

    [Fact(DisplayName = "TD-APPUI-EXTRA-001 exposes extra value, tolerance, location, and review fields")]
    public void Extra_item_exposes_key_properties_and_design_flag()
    {
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:9", GeometryType = "Line", MeasuredValue = 12.5, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .2, ToleranceUnit = "mm", LocationX = 1, LocationY = 2, Review = new DimensionReview { Comment = "Show on drawing", RequiresDesignUpdate = true } });
        Assert.True(item.IsExtra); Assert.Equal("Extra Dimension", item.StatusLabel); Assert.Equal("DarkOrange", item.StatusColor); Assert.Equal("Edge:9", item.GeometryReference); Assert.Equal("12.5", item.MeasuredValue); Assert.Equal("-0.1 / +0.2 mm", item.Tolerance); Assert.Equal("(1, 2)", item.Location); Assert.Equal("Show on drawing", item.Comment); Assert.True(item.RequiresDesignUpdate);
    }
}
