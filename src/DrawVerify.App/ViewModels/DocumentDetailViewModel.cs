namespace DrawVerify.App.ViewModels;
using DrawVerify.Core.Models;

public sealed class DocumentDetailViewModel
{
    public Document? Document { get; private set; }
    public GeometryStatus GeometryStatus => Document?.GeometryStatus ?? GeometryStatus.NotExtracted;
    public string? GeometryError => Document?.GeometryError;
    public void Load(Document document) => Document = document;
}
