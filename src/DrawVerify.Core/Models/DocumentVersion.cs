namespace DrawVerify.Core.Models;

public sealed class DocumentVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public int VersionNumber { get; init; } = 1;
    public string StoredFilePath { get; init; } = string.Empty;
    public GeometryStatus GeometryStatus { get; set; }
    public string? GeometryError { get; set; }
    public GeometryMeasurement? Geometry { get; set; }
    public List<FeatureMeasurement> Features { get; } = new();
}
