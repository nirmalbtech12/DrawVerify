namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Flatness control evaluation rule.
/// </summary>
public sealed class FlatnessRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Flatness ? throw new ArgumentException("FlatnessRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) ? VerificationStatus.NotVerified : VerificationStatus.Passed;
}
