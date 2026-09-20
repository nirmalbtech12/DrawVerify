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
    DxfValidationCompleted = 29, DxfDownloaded = 30, DxfReplaced = 31,
    SubmissionSucceeded = 32, SubmissionCancelled = 33, SubmissionRejected = 34,
    SubmissionSuperseded = 35,
    ReviewStarted = 36,
    Approved = 37,
    Rejected = 38,
    ChangesRequested = 39,
    ReviewCommentAdded = 40,
    FinalApproved = 41,
    ReturnedForRework = 42,
    AccessChanged = 43,
    ReviewCommentDraftSaved = 44,
    ReviewCommentEdited = 45,
    ReviewCommentDiscarded = 46,
    RejectedToChecker = 47,
    RejectedToMaker = 48
    ,DocumentCreated = 49
    ,FileUploaded = 50
    ,FileReplaced = 51
    ,MetadataUpdated = 52
    ,ExtractionCompleted = 53
    ,VerificationRunCompleted = 54
    ,VerificationRunFailed = 55
    ,SubmittedForReview = 56
    ,Returned = 57
    ,DocumentViewed = 58
    ,DocumentDownloaded = 59
    ,DocumentPrinted = 60
    ,DocumentArchived = 61
    ,DocumentDeleted = 62
    ,PriorVersionViewed = 63
    ,IntegrityCheckFailed = 64
    ,MakerSignatureApplied = 65
    ,CheckerSignatureApplied = 66
    ,ApproverSignatureApplied = 67
    ,InternalApprovalStampApplied = 68
    ,AccessGranted = 69
    ,AccessRevoked = 70
    ,AccessModified = 71
    ,SharedLinkCreated = 72
    ,SharedLinkRevoked = 73
    ,SharedLinkRegenerated = 74
    ,AccessedViaSharedLink = 75
    ,CheckerNotificationSent = 76
    ,ApproverNotificationSent = 77
    ,MakerNotificationSent = 78
}
