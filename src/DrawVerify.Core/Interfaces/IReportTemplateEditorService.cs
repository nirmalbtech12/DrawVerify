using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IReportTemplateEditorService
{
    Task<TemplateEditorDocument> OpenAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default);
    Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default);
    Task<ReportTemplate> SaveAsync(ReportTemplate template, TemplateEditorDocument document, int expectedVersion, string actorId, CancellationToken cancellationToken = default);
    Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default);
}
