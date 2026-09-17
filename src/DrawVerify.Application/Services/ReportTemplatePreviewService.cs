using DrawVerify.Core.Interfaces; using DrawVerify.Core.Models;
namespace DrawVerify.Application.Services;
public sealed class ReportTemplatePreviewService : IReportTemplatePreviewService
{
    private readonly IPdfReportGenerator _renderer; public ReportTemplatePreviewService(IPdfReportGenerator renderer) => _renderer = renderer;
    public Task<byte[]> PreviewAsync(ReportTemplate template, CancellationToken cancellationToken = default) { ReportTemplateValidator.Validate(template); var report = new VerificationReport { Metadata = new VerificationReportMetadata { DocumentName = "Sample verification", GeneratedUtc = DateTimeOffset.UtcNow, TemplateId = template.Id, TemplateVersion = template.Version }, AuditTrail = [new AuditTrailEntry { Details = "Sample audit entry", Timestamp = DateTimeOffset.UtcNow }] }; return _renderer.GeneratePdfAsync(report, template, cancellationToken); }
}
