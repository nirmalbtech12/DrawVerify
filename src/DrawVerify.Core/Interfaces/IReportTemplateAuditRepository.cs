using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IReportTemplateAuditRepository { Task<IReadOnlyList<ReportTemplateAuditEntry>> GetAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default); }
