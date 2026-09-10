namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Perpendicularity control evaluation rule.
/// </summary>
public sealed class PerpendicularityRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Perpendicularity ? throw new ArgumentException("PerpendicularityRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) || result.DatumReferences.Count == 0 ? VerificationStatus.NotVerified : VerificationStatus.Passed;
}
