namespace DrawVerify.Core.Models;

public sealed record ReportTemplateDocument
{
    public Guid TemplateId { get; init; }
    public int Version { get; init; }
    public Guid SourceId { get; init; }
    public string SourceFileName { get; init; } = "source.pdf";
    public byte[] Content { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public TemplateMappingSnapshot? TemplateMapping { get; init; }
    public byte[] DocxBytes { get => Content; init => Content = value; }
}
