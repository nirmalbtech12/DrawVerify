namespace DrawVerify.Core.Models;

public sealed record TemplateEditorDocument
{
    public byte[] DocxBytes { get; init; } = [];
    public IReadOnlyList<TemplatePlaceholder> Placeholders { get; init; } = [];
    public bool IsEditable { get; init; } = true;
    public bool PlaceholderToolsEnabled { get; init; } = true;
}
