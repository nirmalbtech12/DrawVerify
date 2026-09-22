using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IWordTemplateEditor
{
    Task<TemplateEditorDocument> OpenAsync(byte[] docxBytes, CancellationToken cancellationToken = default);
    Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default);
    Task<byte[]> SaveAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default);
}
