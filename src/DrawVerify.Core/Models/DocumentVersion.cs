namespace DrawVerify.Core.Models;

public sealed class DocumentVersion
{
    public Guid VersionId { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public int VersionNumber { get; init; }
    public string StoredRelativePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string UploaderUserId { get; init; } = string.Empty;
}
