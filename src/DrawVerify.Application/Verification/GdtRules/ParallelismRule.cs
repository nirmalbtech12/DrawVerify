namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;

/// <summary>
/// Parallelism control evaluation rule.
/// </summary>
public sealed class ParallelismRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Parallelism ? throw new ArgumentException("ParallelismRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) || result.DatumReferences.Count == 0 ? VerificationStatus.NotVerified : VerificationStatus.Passed;
    public IReadOnlyList<string> Compare(GdtAnnotationData step, GdtAnnotationData dxf) => Differences(step, dxf);
    private static IReadOnlyList<string> Differences(GdtAnnotationData a, GdtAnnotationData b)
    {
        var differences = new List<string>();
        if (!Equal(a.ControlledFeature, b.ControlledFeature)) differences.Add("controlled feature");
        if (!Equal(a.ToleranceValue, b.ToleranceValue)) differences.Add("tolerance");
        if (!Equal(a.ToleranceZoneType, b.ToleranceZoneType) || !Equal(a.ToleranceZoneDiameter, b.ToleranceZoneDiameter)) differences.Add("tolerance zone");
        if (!Equal(a.NormalizedModifiers ?? a.Modifiers, b.NormalizedModifiers ?? b.Modifiers)) differences.Add("modifiers");
        if (!a.DatumReferences.SequenceEqual(b.DatumReferences, StringComparer.OrdinalIgnoreCase)) differences.Add("datum reference order");
        if (!string.IsNullOrWhiteSpace(a.ComparisonCriteria) && !string.IsNullOrWhiteSpace(b.ComparisonCriteria) && !Equal(a.ComparisonCriteria, b.ComparisonCriteria)) differences.Add("comparison criteria");
        return differences;
    }
    private static bool Equal(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
