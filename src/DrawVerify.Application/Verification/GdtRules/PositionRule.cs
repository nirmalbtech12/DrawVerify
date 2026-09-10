namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Position tolerance control evaluation rule (ASME Y14.5 position).
/// </summary>
public sealed class PositionRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType == GdtControlType.Position ? VerificationStatus.Passed : throw new ArgumentException("PositionRule received a different control type.", nameof(result));
}
