using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record Document
{
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public string OriginalFileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public long FileSize => FileSizeBytes;
    public DateTime UploadTimestampUtc { get; init; } = DateTime.UtcNow;
    public DateTime UploadTimestamp => UploadTimestampUtc;
    public string UploaderUserId { get; init; } = string.Empty;
    public string UploaderDisplayName { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; } = DocumentType.StepModel;
    public UploadStatus UploadStatus { get; init; } = UploadStatus.Uploaded;
    public GeometryStatus GeometryStatus { get; init; } = GeometryStatus.PendingValidation;
    public DocumentStatus Status { get; init; } = DocumentStatus.Active;
    public int CurrentVersion { get; init; } = 1;
    public DocumentVersion? Version { get; init; }
    public IReadOnlyList<DocumentVersion> Versions { get; init; } = Array.Empty<DocumentVersion>();
}
