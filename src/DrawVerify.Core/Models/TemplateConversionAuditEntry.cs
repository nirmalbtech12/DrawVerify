namespace DrawVerify.Core.Models;

public sealed record TemplateConversionAuditEntry
{
    public Guid AuditId { get; init; } = Guid.NewGuid();
    public Guid SourceId { get; init; }
    public string SourceFileName { get; init; } = "source.pdf";
    public Guid? TemplateId { get; init; }
    public int? Version { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ActorId { get; init; } = "Unknown";
    public string Status { get; init; } = "Failed";
    public string? ErrorDetails { get; init; }
    public string SourcePdfName { get => SourceFileName; init => SourceFileName = value; }
}
