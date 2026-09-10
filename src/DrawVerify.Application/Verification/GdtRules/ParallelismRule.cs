namespace DrawVerify.Application.Verification.GdtRules;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

/// <summary>
/// Parallelism control evaluation rule.
/// </summary>
public sealed class ParallelismRule
{
    public VerificationStatus Evaluate(GdtResult result) => result.ControlType == GdtControlType.Parallelism ? VerificationStatus.Passed : throw new ArgumentException("ParallelismRule received a different control type.", nameof(result));
}
