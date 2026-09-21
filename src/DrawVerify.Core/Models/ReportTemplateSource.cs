namespace DrawVerify.Core.Models;

public sealed record ReportTemplateSource
{
    public Guid SourceId { get; init; } = Guid.NewGuid();
    public string FileName { get; init; } = "source.pdf";
    public byte[] PdfBytes { get; init; } = [];
    public string? ContentType { get; init; } = "application/pdf";
    public Guid SourcePdfId { get => SourceId; init => SourceId = value; }
    public string SourcePdfName { get => FileName; init => FileName = value; }
    public byte[] Content { get => PdfBytes; init => PdfBytes = value; }
}
