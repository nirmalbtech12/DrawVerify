namespace DrawVerify.Core.Enums;

public enum VerificationStatus
{
    Unknown = 0,
    PendingVerification = 1,
    DimensionsExtracted = 2,
    Error = 3,
    NotVerified = 4,
    Passed = 5,
    Failed = 6,
    Matched = 7,
    Missing = 8,
    Extra = 9,
}
