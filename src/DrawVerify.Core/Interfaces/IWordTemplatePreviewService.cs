using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IWordTemplatePreviewService
{
    Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default);
}
