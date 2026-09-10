namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;

/// <summary>
/// Position tolerance control evaluation rule (ASME Y14.5 position).
/// </summary>
public sealed class PositionRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Position ? throw new ArgumentException("PositionRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) ? VerificationStatus.NotVerified : VerificationStatus.Passed;
    public IReadOnlyList<string> Compare(GdtAnnotationData step, GdtAnnotationData dxf) => Differences(step, dxf, includeNominal: true, includeDatums: true);
    private static IReadOnlyList<string> Differences(GdtAnnotationData a, GdtAnnotationData b, bool includeNominal, bool includeDatums)
    {
        var differences = new List<string>();
        if (!Equal(a.ControlledFeature, b.ControlledFeature)) differences.Add("controlled feature");
        if (includeNominal && !Equal(a.NominalBasicLocation, b.NominalBasicLocation)) differences.Add("nominal/basic location");
        if (!Equal(a.ToleranceValue, b.ToleranceValue)) differences.Add("tolerance");
        if (!Equal(a.ToleranceZoneType, b.ToleranceZoneType) || !Equal(a.ToleranceZoneDiameter, b.ToleranceZoneDiameter)) differences.Add("tolerance zone");
        if (!Equal(a.NormalizedModifiers ?? a.Modifiers, b.NormalizedModifiers ?? b.Modifiers)) differences.Add("modifiers");
        if (includeDatums && !a.DatumReferences.SequenceEqual(b.DatumReferences, StringComparer.OrdinalIgnoreCase)) differences.Add("datum reference order");
        if (!string.IsNullOrWhiteSpace(a.ComparisonCriteria) && !string.IsNullOrWhiteSpace(b.ComparisonCriteria) && !Equal(a.ComparisonCriteria, b.ComparisonCriteria)) differences.Add("comparison criteria");
        return differences;
    }
    private static bool Equal(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
