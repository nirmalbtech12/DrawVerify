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
    UnmatchedDimensionsFlagged = 15,
    ToleranceComparisonApplied = 16, VerificationReportGenerated = 17,
    CommentAdded = 18, StatusChanged = 19,
    UnsupportedGdtDetected = 20, UnsupportedGdtReviewChanged = 21,
    GdtValidationCompleted = 22, GdtReviewChanged = 23,
    FlaggedCannotValidate = 24, DispositionChanged = 25,
    DxfGenerationStarted = 26, DxfGenerationCompleted = 27, DxfGenerationFailed = 28,
    DxfValidationCompleted = 29, DxfDownloaded = 30, DxfReplaced = 31
}
