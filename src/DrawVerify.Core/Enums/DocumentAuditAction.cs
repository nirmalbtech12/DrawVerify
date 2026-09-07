namespace DrawVerify.Core.Enums;

public enum DocumentAuditAction
{
    Uploaded = 1, DuplicateDetected = 2, Overwritten = 3, Cancelled = 4,
    MeasurementsExtracted = 5, MeasurementExtractionFailed = 6, InvalidStepFileRejected = 7,
    InvalidDxfFileRejected = 8
}
