namespace DrawVerify.Core.Models;

public sealed record TemplatePreviewData
{
    public byte[] DocxBytes { get; init; } = [];
    public IReadOnlyDictionary<Guid, string> Samples { get; init; } = new Dictionary<Guid, string>();
}
