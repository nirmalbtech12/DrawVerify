namespace DrawVerify.Core.Models;

public sealed class DocumentVersion
{
<<<<<<< HEAD
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public int VersionNumber { get; init; } = 1;
    public string StoredFilePath { get; init; } = string.Empty;
    public GeometryStatus GeometryStatus { get; set; }
    public string? GeometryError { get; set; }
    public GeometryMeasurement? Geometry { get; set; }
    public List<FeatureMeasurement> Features { get; } = new();
=======
    public Guid VersionId { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public int VersionNumber { get; init; }
    public string StoredRelativePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string UploaderUserId { get; init; } = string.Empty;
>>>>>>> 642060c (KAN-19: implementation)
}
