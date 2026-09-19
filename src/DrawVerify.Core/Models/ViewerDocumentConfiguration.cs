namespace DrawVerify.Core.Models;

public sealed class ViewerDocumentConfiguration
{
    public bool AllowDownload { get; init; }
    public ViewerAccessConfiguration Access { get; init; } = new();
}
