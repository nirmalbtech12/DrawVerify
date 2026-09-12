using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record Document
{
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public string OriginalFileName { get; init; } = string.Empty;
    public string Name { get => OriginalFileName; init => OriginalFileName = value; }
    public Guid Id => DocumentId;
    public long FileSizeBytes { get; init; }
    public long FileSize => FileSizeBytes;
    public DateTime UploadTimestampUtc { get; init; } = DateTime.UtcNow;
    public DateTime UploadTimestamp => UploadTimestampUtc;
    public DrawVerify.Core.Enums.DocumentType DocumentType { get; init; } = DrawVerify.Core.Enums.DocumentType.StepModel;
    public DrawingSource DrawingSource { get; init; } = DrawingSource.Unknown;
    public DrawVerify.Core.Enums.UploadStatus UploadStatus { get; set; } = DrawVerify.Core.Enums.UploadStatus.Uploaded;
    public DrawVerify.Core.Enums.VerificationStatus VerificationStatus { get; init; } = DrawVerify.Core.Enums.VerificationStatus.Unknown;
    public string? VerificationError { get; init; }
    public string? UploaderUserId { get; init; }
    public string? UploaderDisplayName { get; init; }
    public string? UploaderDisplayNameSnapshot => UploaderDisplayName;
    public DrawVerify.Core.Enums.GeometryStatus GeometryStatus { get; set; } = DrawVerify.Core.Enums.GeometryStatus.PendingValidation;
    public string? GeometryError { get; set; }
    public DocumentStatus Status { get; init; } = DocumentStatus.Active;
    public int CurrentVersion { get; init; } = 1;
    public DocumentVersion? Version { get; init; }
    public IReadOnlyList<DocumentVersion> Versions { get; init; } = Array.Empty<DocumentVersion>();
}
