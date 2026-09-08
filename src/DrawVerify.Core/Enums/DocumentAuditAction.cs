namespace DrawVerify.Core.Enums;

public enum DocumentAuditAction
{
    Uploaded = 1, DuplicateDetected = 2, Overwritten = 3, Cancelled = 4,
    MeasurementsExtracted = 5, MeasurementExtractionFailed = 6, InvalidStepFileRejected = 7,
    InvalidDxfFileRejected = 8,
    DimensionsExtracted = 9,
    DimensionsExtractedPartial = 10,
    DimensionExtractionFailed = 11,
    DimensionsMatched = 12,
    DimensionsMatchedPartial = 13,
    DimensionMatchingFailed = 14,
    UnmatchedDimensionsFlagged = 15
}
