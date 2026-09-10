namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;

/// <summary>
/// Perpendicularity control evaluation rule.
/// </summary>
public sealed class PerpendicularityRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Perpendicularity ? throw new ArgumentException("PerpendicularityRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) || result.DatumReferences.Count == 0 ? VerificationStatus.NotVerified : VerificationStatus.Passed;
    public IReadOnlyList<string> Compare(GdtAnnotationData step, GdtAnnotationData dxf)
    {
        var differences = new List<string>();
        if (!Equal(step.ControlledFeature, dxf.ControlledFeature)) differences.Add("controlled feature");
        if (!Equal(step.ToleranceValue, dxf.ToleranceValue)) differences.Add("tolerance");
        if (!Equal(step.ToleranceZoneType, dxf.ToleranceZoneType) || !Equal(step.ToleranceZoneDiameter, dxf.ToleranceZoneDiameter)) differences.Add("tolerance zone");
        if (!Equal(step.NormalizedModifiers ?? step.Modifiers, dxf.NormalizedModifiers ?? dxf.Modifiers)) differences.Add("modifiers");
        if (!step.DatumReferences.SequenceEqual(dxf.DatumReferences, StringComparer.OrdinalIgnoreCase)) differences.Add("datum reference order");
        if (!string.IsNullOrWhiteSpace(step.ComparisonCriteria) && !string.IsNullOrWhiteSpace(dxf.ComparisonCriteria) && !Equal(step.ComparisonCriteria, dxf.ComparisonCriteria)) differences.Add("comparison criteria");
        return differences;
    }
    private static bool Equal(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
