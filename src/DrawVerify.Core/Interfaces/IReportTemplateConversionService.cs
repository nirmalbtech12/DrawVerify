using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IReportTemplateConversionService
{
    Task<ReportTemplate> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default);
    Task<ReportTemplate> ReconvertAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<ReportTemplate> ConvertPdfAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) => ConvertAsync(source, cancellationToken);
    Task<ReportTemplate> ReconvertFromSourceAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken = default) => ReconvertAsync(templateId, expectedVersion, cancellationToken);
}
