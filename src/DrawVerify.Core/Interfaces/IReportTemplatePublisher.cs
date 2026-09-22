using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IReportTemplatePublisher
{
    Task<ReportTemplate> PublishAsync(ReportTemplate template, int expectedVersion, ReportTemplateAuditEntry audit, ReportTemplateDocument document, CancellationToken cancellationToken = default);
}
