using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class GdtResultTests
{
    [Fact(DisplayName = "TD-01 keeps unsupported results separate from dimension statuses")]
    public void Unsupported_result_has_exact_reason_and_separate_count()
    {
        var unsupported = GdtResult.Unsupported("gdt-1", GdtControlType.Unknown, "◇", "drawing.dxf", "entity:4");
        var dimensions = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched },
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched },
            new DimensionMatch { Status = DimensionMatchStatus.MissingDimension },
            new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension }
        };

        var summary = ComparisonSummary.From(dimensions, new[] { unsupported });

        Assert.Equal(VerificationStatus.NotVerified, unsupported.Status);
        Assert.Equal(GdtResult.UnsupportedReason, unsupported.Reason);
        Assert.Equal(1, summary.NotVerifiedGdtControls);
        Assert.Equal(1, summary.MatchedDimensions);
        Assert.Equal(1, summary.MismatchedDimensions);
        Assert.Equal(1, summary.MissingDimensions);
        // The legacy summary intentionally groups MissingDimension and ExtraDimension
        // under ExtraDimensions; NotVerifiedGdtControls remains independently counted.
        Assert.Equal(2, summary.ExtraDimensions);
    }

    [Fact(DisplayName = "TD-01 preserves Not Verified during review")]
    public void Review_metadata_does_not_change_status()
    {
        var result = GdtResult.Unsupported("gdt-2", GdtControlType.Flatness, "⏥", "model.step", "pmi:2");
        var reviewed = result.WithReview("Needs engineering review", true);

        Assert.Equal(VerificationStatus.NotVerified, reviewed.Status);
        Assert.Equal("Needs engineering review", reviewed.ReviewComment);
        Assert.True(reviewed.RequiresEngineeringReview);
        Assert.Equal(result.GdtResultId, reviewed.GdtResultId);
    }

    [Fact(DisplayName = "TD-01 rejects invalid unsupported result input")]
    public void Unsupported_result_requires_identifier_and_source()
    {
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("", sourceDocument: "drawing.dxf"));
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("gdt-3"));
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("gdt-4", (GdtControlType)99, sourceDocument: "drawing.dxf"));
    }

    [Fact(DisplayName = "TD-06 counts Not Verified GD&T separately from dimension outcomes")]
    public void Not_verified_count_does_not_change_dimension_totals()
    {
        var dimensions = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 1 },
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 2 }
        };
        var summary = ComparisonSummary.From(dimensions, new[] { GdtResult.Unsupported("gdt-count", sourceDocument: "drawing.dxf") });

        Assert.Equal(2, summary.TotalDimensions);
        Assert.Equal(1, summary.MatchedDimensions);
        Assert.Equal(1, summary.MismatchedDimensions);
        Assert.Equal(1, summary.NotVerifiedGdtControls);
    }
}
