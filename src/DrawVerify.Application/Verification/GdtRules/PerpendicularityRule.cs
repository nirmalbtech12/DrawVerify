namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Perpendicularity control evaluation rule.
/// </summary>
public sealed class PerpendicularityRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType == GdtControlType.Perpendicularity ? VerificationStatus.Passed : throw new ArgumentException("PerpendicularityRule received a different control type.", nameof(result));
}
