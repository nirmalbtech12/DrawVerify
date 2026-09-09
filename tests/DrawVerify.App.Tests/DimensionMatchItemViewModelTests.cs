using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Windows.Media;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DimensionMatchItemViewModelTests
{
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
}
