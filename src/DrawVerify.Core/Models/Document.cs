namespace DrawVerify.Core.Models;

public sealed class Document
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; }
    public UploadStatus UploadStatus { get; set; }
    public Guid CurrentVersionId { get; set; }
    public GeometryStatus GeometryStatus { get; set; }
    public string? GeometryError { get; set; }
}
