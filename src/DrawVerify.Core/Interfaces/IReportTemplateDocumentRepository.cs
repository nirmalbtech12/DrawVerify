using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IReportTemplateDocumentRepository
{
    Task<ReportTemplateDocument> SaveAsync(ReportTemplateDocument document, int? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<ReportTemplateDocument?> GetAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default);
    Task<ReportTemplateSource?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
    Task SaveSourceAsync(ReportTemplateSource source, CancellationToken cancellationToken = default);
    Task RecordConversionAuditAsync(TemplateConversionAuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TemplateConversionAuditEntry>> GetConversionAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default);
}
