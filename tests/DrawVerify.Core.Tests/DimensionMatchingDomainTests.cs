using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class DimensionMatchingDomainTests
{
    [Fact(DisplayName = "TD-CORE-001 calculates signed and absolute deltas with dimension units")]
    public void Mismatched_match_calculates_directional_delta_without_rounding()
    {
        var linear = new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.85 };
        var radial = new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Radial, NominalValue = 20, MeasuredValue = 20.3 };
        var angular = new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Angular, NominalValue = 45, MeasuredValue = 42.5 };
        Assert.Equal(.15, linear.SignedDelta!.Value, 12); Assert.Equal(.15, linear.AbsoluteDelta!.Value, 12); Assert.Equal("mm", linear.DeltaUnit);
        Assert.Equal(-.3, radial.SignedDelta!.Value, 12); Assert.Equal(.3, radial.AbsoluteDelta!.Value, 12); Assert.Equal("mm", radial.DeltaUnit);
        Assert.Equal(2.5, angular.SignedDelta!.Value, 12); Assert.Equal(2.5, angular.AbsoluteDelta!.Value, 12); Assert.Equal("°", angular.DeltaUnit);
    }

    [Fact(DisplayName = "TD-CORE-002 does not fabricate deltas for invalid inputs")]
    public void Invalid_delta_inputs_are_null()
    {
        foreach (var match in new[]
        {
            new DimensionMatch { NominalValue = null, MeasuredValue = 1 },
            new DimensionMatch { NominalValue = double.NaN, MeasuredValue = 1 },
            new DimensionMatch { NominalValue = double.PositiveInfinity, MeasuredValue = 1 },
            new DimensionMatch { NominalValue = 1, MeasuredValue = double.NegativeInfinity }
        })
        { Assert.Null(match.SignedDelta); Assert.Null(match.AbsoluteDelta); Assert.Null(match.DeltaMagnitude); }
    }

    [Fact(DisplayName = "TD-CORE-003 renders only mismatched dimensions with complete delta fields")]
    public void Markdown_contains_mismatched_delta_section_and_excludes_other_statuses()
    {
        var markdown = new VerificationReport { DimensionMatches = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 1, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.85 },
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 2, DimensionType = DimensionType.Angular, NominalValue = 45, MeasuredValue = 42.5 },
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 3, NominalValue = 99, MeasuredValue = 99 }
        }}.ToMarkdown();
        Assert.Contains("## Mismatched Dimensions with Delta", markdown);
        Assert.Contains("DXF nominal: 10 mm", markdown); Assert.Contains("STEP measured: 9.85 mm", markdown);
        Assert.Contains("Absolute delta: 0.15 mm", markdown); Assert.Contains("Signed delta (DXF − STEP): +0.15 mm", markdown);
        Assert.Contains("Signed delta (DXF − STEP): +2.5 °", markdown); Assert.DoesNotContain("Reference: `3`", markdown);
    }
    [Fact(DisplayName = "TD-CORE-001 preserves the dimension-match contract and audit enum compatibility")]
    public void Legacy_dimension_constructor_defaults_radial_kind_to_unknown()
    {
        var dimension = new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 1, DimensionType.Radial, 5);
        Assert.Equal(RadialDimensionKind.Unknown, dimension.RadialKind);
    }

    [Fact(DisplayName = "TD-CORE-001 preserves explicit unmatched metadata and rejects invalid configuration")]
    public void Configuration_rejects_invalid_limits_and_thresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { TimeoutSeconds = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { MaximumStepFileSizeBytes = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new DimensionMatchingConfiguration { HighConfidenceThreshold = .5, MediumConfidenceThreshold = .75 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { ToleranceThresholdMm = double.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { AngularToleranceDegrees = -1 }.Validate());
        Assert.Equal(.1, new DimensionMatchingConfiguration().ToleranceThresholdMm);
        Assert.Equal(1, new DimensionMatchingConfiguration().AngularToleranceDegrees);
    }

    [Fact(DisplayName = "TD-CORE-001 limits dimension matching permissions to Maker and Checker")]
    public void Only_maker_and_checker_can_match_or_view_results()
    {
        var authorization = new RoleAuthorization();
        Assert.False(authorization.IsAllowed(null, RolePermission.MatchDimensionsToGeometry));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("maker", new[] { "Maker" }), RolePermission.MatchDimensionsToGeometry));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("checker", new[] { "Checker" }), RolePermission.ViewDimensionMatchResults));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("viewer", new[] { "Viewer" }), RolePermission.ViewDimensionMatchResults));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("approver", new[] { "Approver" }), RolePermission.MatchDimensionsToGeometry));
    }

    [Fact(DisplayName = "TD-CORE-001 accepts explicit unmatched status, reason, and DXF metadata")]
    public void Match_record_requires_explicit_status_and_supports_manual_review_fields()
    {
        var match = new DimensionMatch { VerificationSessionId = Guid.NewGuid(), Status = DimensionMatchStatus.Unmatched, RequiresManualReview = true, ReasonCode = "NoCorrespondingGeometryFound", ReasonExplanation = "No corresponding geometry found in STEP model.", DimensionType = DimensionType.Linear, NominalValue = 12.5, DimensionText = "Ø12.5" };
        Assert.Equal(DimensionMatchStatus.Unmatched, match.Status);
        Assert.True(match.RequiresManualReview);
        Assert.Equal("NoCorrespondingGeometryFound", match.ReasonCode);
        Assert.Equal("No corresponding geometry found in STEP model.", match.ReasonExplanation);
        Assert.Equal(DimensionType.Linear, match.DimensionType);
        Assert.Equal(12.5, match.NominalValue);
        Assert.Equal("Ø12.5", match.DimensionText);
        Assert.Null(match.StepGeometryReference);
    }

    [Fact(DisplayName = "TD-CORE-001 appends UnmatchedDimensionsFlagged without changing existing audit values")]
    public void Unmatched_audit_action_is_appended_without_changing_existing_values()
    {
        Assert.Equal(12, (int)DocumentAuditAction.DimensionsMatched);
        Assert.Equal(13, (int)DocumentAuditAction.DimensionsMatchedPartial);
        Assert.Equal(14, (int)DocumentAuditAction.DimensionMatchingFailed);
        Assert.Equal(15, (int)DocumentAuditAction.UnmatchedDimensionsFlagged);
        Assert.Equal(16, (int)DocumentAuditAction.ToleranceComparisonApplied);
    }

    [Fact(DisplayName = "TD-CORE-001 preserves explicit symmetric and asymmetric tolerance bounds")]
    public void Dimension_data_preserves_explicit_tolerance_bounds()
    {
        var id = Guid.NewGuid();
        var symmetric = new DimensionData(id, Guid.NewGuid(), 1, DimensionType.Linear, 10, -.05, .05);
        var asymmetric = new DimensionData(id, Guid.NewGuid(), 2, DimensionType.Linear, 10, -.2, .1);
        Assert.Equal((-.05, .05), (symmetric.LowerTolerance, symmetric.UpperTolerance));
        Assert.Equal((-.2, .1), (asymmetric.LowerTolerance, asymmetric.UpperTolerance));
        Assert.Equal(DimensionType.Angular, new DimensionData(id, Guid.NewGuid(), 3, DimensionType.Angular, 45).DimensionType);
    }

    [Fact(DisplayName = "TD-CORE-001 rejects non-finite and reversed explicit tolerance bounds")]
    public void Dimension_data_rejects_invalid_explicit_bounds()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(id, Guid.NewGuid(), 1, DimensionType.Linear, 10, double.PositiveInfinity, .1));
        Assert.Throws<ArgumentException>(() => new DimensionData(id, Guid.NewGuid(), 1, DimensionType.Linear, 10, .1, -.1));
    }
}
