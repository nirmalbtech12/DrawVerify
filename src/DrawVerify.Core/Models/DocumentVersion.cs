using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class DocumentVersion
{
    public Guid VersionId { get; init; } = Guid.NewGuid();
    public Guid Id { get => VersionId; init => VersionId = value; }
    public Guid DocumentId { get; init; }
    public DrawingSource DrawingSource { get; init; } = DrawingSource.Unknown;
    public int VersionNumber { get; init; }
    public string StoredRelativePath { get; init; } = string.Empty;
    public string StoredFilePath { get => StoredRelativePath; init => StoredRelativePath = value; }
    public long FileSizeBytes { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DrawVerify.Core.Enums.GeometryStatus GeometryStatus { get; set; } = DrawVerify.Core.Enums.GeometryStatus.NotExtracted;
    public string? GeometryError { get; set; }
    public GeometryMeasurement? Geometry { get; set; }
    public List<FeatureMeasurement> Features { get; } = new();
}
