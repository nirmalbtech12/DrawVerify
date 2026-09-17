using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplatePreviewServiceTests
{
    [Fact(DisplayName = "TSD-AC09 preview uses unsaved template and deterministic sample markers")]
    public async Task Preview_passes_current_template_to_renderer_without_persistence()
    {
        var renderer = new CapturingRenderer();
        var template = new ReportTemplate { Header = new() { OrganizationName = "Unsaved" } };
        var bytes = await new ReportTemplatePreviewService(renderer).PreviewAsync(template);
        Assert.Equal(new byte[] { 37, 80, 68, 70 }, bytes);
        Assert.Equal(template.Id, renderer.Report!.Metadata.TemplateId); Assert.Equal("Sample verification", renderer.Report.Metadata.DocumentName);
        Assert.Contains(renderer.Report.AuditTrail, x => x.Details == "Sample audit entry");
    }

    private sealed class CapturingRenderer : IPdfReportGenerator
    {
        public VerificationReport? Report { get; private set; }
        public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default) { Report = report; return Task.FromResult(new byte[] { 37, 80, 68, 70 }); }
    }
}
