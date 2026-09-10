namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Parallelism control evaluation rule.
/// </summary>
public sealed class ParallelismRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType != GdtControlType.Parallelism ? throw new ArgumentException("ParallelismRule received a different control type.", nameof(result)) : string.IsNullOrWhiteSpace(result.ToleranceValue) || string.IsNullOrWhiteSpace(result.ControlledFeature) || result.DatumReferences.Count == 0 ? VerificationStatus.NotVerified : VerificationStatus.Passed;
}
