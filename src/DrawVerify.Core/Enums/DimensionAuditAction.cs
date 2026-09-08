namespace DrawVerify.Core.Enums;

/// <summary>Audit actions emitted by dimension-to-geometry matching.</summary>
public enum DimensionAuditAction
{
    DimensionsMatched = 1,
    DimensionsMatchedPartial = 2,
    DimensionMatchingFailed = 3
}
