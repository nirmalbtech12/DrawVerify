using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Verification;

public sealed record ToleranceComparison(bool IsWithinTolerance, double Lower, double Upper, string Unit, ToleranceSource Source);

public static class ToleranceComparer
{
    private const double FloatingPointGuard = 1e-9;

    public static ToleranceComparison Compare(DimensionData dimension, double measuredValue, DimensionMatchingConfiguration configuration, double? sessionToleranceMm = null)
    {
        if (!double.IsFinite(measuredValue)) throw new ArgumentOutOfRangeException(nameof(measuredValue));
        configuration.Validate();
        var angular = dimension.DimensionType == DimensionType.Angular;
        var unit = angular ? "degrees" : "mm";
        double lower;
        double upper;
        ToleranceSource source;
        if (dimension.LowerTolerance is double explicitLower && dimension.UpperTolerance is double explicitUpper && double.IsFinite(explicitLower) && double.IsFinite(explicitUpper) && explicitLower <= explicitUpper)
        {
            lower = explicitLower; upper = explicitUpper; source = ToleranceSource.Explicit;
        }
        else
        {
            var tolerance = angular ? configuration.AngularToleranceDegrees : sessionToleranceMm ?? configuration.ToleranceThresholdMm;
            if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(sessionToleranceMm));
            lower = -tolerance; upper = tolerance; source = sessionToleranceMm is not null && !angular ? ToleranceSource.Session : ToleranceSource.Default;
        }
        var expectedLower = dimension.NominalValue + lower;
        var expectedUpper = dimension.NominalValue + upper;
        return new ToleranceComparison(measuredValue >= expectedLower - FloatingPointGuard && measuredValue <= expectedUpper + FloatingPointGuard, lower, upper, unit, source);
    }
}
