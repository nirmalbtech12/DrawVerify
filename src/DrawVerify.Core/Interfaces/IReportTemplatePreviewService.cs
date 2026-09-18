using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IReportTemplatePreviewService { Task<byte[]> PreviewAsync(ReportTemplate template, CancellationToken cancellationToken = default); }
