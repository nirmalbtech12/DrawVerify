using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Application.Verification;
using Xunit;

namespace DrawVerify.Application.Tests;

/// <summary>
/// Tests for ToleranceComparer (measured value vs tolerance range).
/// </summary>
public sealed class ToleranceComparerTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();

    private static DimensionData Dimension(double nominal, DimensionType type = DimensionType.Linear, double? lower = null, double? upper = null, RadialDimensionKind radial = RadialDimensionKind.Unknown) =>
        new(DocumentId, VersionId, 1, type, nominal, lower, upper, radialDimensionKind: radial);

    [Fact(DisplayName = "TD-APP-001 uses the default 0.1 mm threshold inclusively")]
    public void Default_linear_threshold_is_inclusive_and_precision_safe()
    {
        var configuration = new DimensionMatchingConfiguration();
        var atLower = ToleranceComparer.Compare(Dimension(10), 9.9, configuration);
        var atUpper = ToleranceComparer.Compare(Dimension(10), 10.1, configuration);
        var outside = ToleranceComparer.Compare(Dimension(10), 10.1001, configuration);
        Assert.True(atLower.IsWithinTolerance); Assert.True(atUpper.IsWithinTolerance); Assert.False(outside.IsWithinTolerance);
        Assert.Equal((-0.1, 0.1), (atUpper.Lower, atUpper.Upper));
        Assert.Equal("mm", atUpper.Unit); Assert.Equal(ToleranceSource.Default, atUpper.Source);
    }

    [Fact(DisplayName = "TD-APP-001 gives session overrides precedence over the global default")]
    public void Session_override_is_used_for_linear_dimensions()
    {
        var result = ToleranceComparer.Compare(Dimension(10), 10.19, new DimensionMatchingConfiguration { ToleranceThresholdMm = .1 }, .2);
        Assert.True(result.IsWithinTolerance); Assert.Equal(ToleranceSource.Session, result.Source); Assert.Equal(.2, result.Upper);
    }

    [Fact(DisplayName = "TD-APP-001 explicit symmetric and asymmetric bounds override defaults")]
    public void Explicit_bounds_are_used_when_present()
    {
        var symmetric = ToleranceComparer.Compare(Dimension(10, lower: -.05, upper: .05), 10.05, new DimensionMatchingConfiguration());
        var asymmetric = ToleranceComparer.Compare(Dimension(10, lower: -.2, upper: .1), 9.8, new DimensionMatchingConfiguration());
        var outside = ToleranceComparer.Compare(Dimension(10, lower: -.2, upper: .1), 10.1001, new DimensionMatchingConfiguration());
        Assert.True(symmetric.IsWithinTolerance); Assert.True(asymmetric.IsWithinTolerance); Assert.False(outside.IsWithinTolerance);
        Assert.Equal(ToleranceSource.Explicit, symmetric.Source); Assert.Equal((-.2, .1), (asymmetric.Lower, asymmetric.Upper));
    }

    [Fact(DisplayName = "TD-APP-001 applies degree and millimetre units to angular and radial dimensions")]
    public void Angular_and_radial_units_are_consistent()
    {
        var angular = ToleranceComparer.Compare(Dimension(45, DimensionType.Angular), 46, new DimensionMatchingConfiguration());
        var radial = ToleranceComparer.Compare(Dimension(10, DimensionType.Radial, radial: RadialDimensionKind.Radius), 10.1, new DimensionMatchingConfiguration());
        Assert.True(angular.IsWithinTolerance); Assert.Equal("degrees", angular.Unit); Assert.Equal(1, angular.Upper);
        Assert.True(radial.IsWithinTolerance); Assert.Equal("mm", radial.Unit);
    }

    [Fact(DisplayName = "TD-APP-001 falls back to defaults when explicit tolerance is absent")]
    public void Absent_explicit_bounds_use_default_source()
    {
        var result = ToleranceComparer.Compare(Dimension(10), 10.099999999, new DimensionMatchingConfiguration());
        Assert.True(result.IsWithinTolerance); Assert.Equal(ToleranceSource.Default, result.Source);
    }
}
