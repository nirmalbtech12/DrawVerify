namespace DrawVerify.Core.Models;

// Compatibility constants for the original measurement-domain namespace.
// The enum itself lives in Core.Enums so upload and measurement workflows share one type.
public static class GeometryStatus
{
    public const DrawVerify.Core.Enums.GeometryStatus NotExtracted = DrawVerify.Core.Enums.GeometryStatus.NotExtracted;
    public const DrawVerify.Core.Enums.GeometryStatus Processing = DrawVerify.Core.Enums.GeometryStatus.PendingValidation;
    public const DrawVerify.Core.Enums.GeometryStatus Processed = DrawVerify.Core.Enums.GeometryStatus.Processed;
    public const DrawVerify.Core.Enums.GeometryStatus Error = DrawVerify.Core.Enums.GeometryStatus.Error;
}
