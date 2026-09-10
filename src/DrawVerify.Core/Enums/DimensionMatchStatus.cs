namespace DrawVerify.Core.Enums;

public enum DimensionMatchStatus
{
    Matched = 1,
    Unmatched = 2,
    ExtraDimension = 3,
    MissingDimension = ExtraDimension,
    Mismatched = 4
}
