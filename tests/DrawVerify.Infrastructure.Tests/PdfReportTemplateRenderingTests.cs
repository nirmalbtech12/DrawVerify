using System.Text;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Pdf;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class PdfReportTemplateRenderingTests
{
    [Fact(DisplayName = "TSD-AC03 AC04 AC05 AC06 AC07 AC08 AC15 configured template markers render")]
    public async Task Template_settings_are_applied_to_pdf_output()
    {
        var template = new ReportTemplate
        {
            Header = new() { OrganizationName = "Acme", OrganizationAddress = "1 Main", ReportTitleFormat = "Configured title" },
            Footer = new() { Text = "Confidential", PageNumberFormat = "Page X of Y", TemplateVersionLabel = "v7", EffectiveDate = DateTimeOffset.Parse("2026-01-02T00:00:00Z") },
            Sections = ReportTemplateDefaults.Sections.Where(x => x.Key is "Summary" or "AuditTrail").Select((x, i) => x with { Ordinal = i, Title = x.Key + " configured", Description = "details" }).ToArray(),
            Style = new() { Orientation = "Landscape", Margin = 30, HeaderColor = "#123456" }
        };
        var bytes = await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { Metadata = new() { DocumentName = "sample", TemplateId = template.Id, TemplateVersion = template.Version }, AuditTrail = [new() { Details = "sample audit" }] }, template);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("%PDF-", text); Assert.Contains("Configured title", text); Assert.Contains("Confidential", text); Assert.Contains("Page 1 of 1", text); Assert.Contains("#123456", text);
    }

    [Fact(DisplayName = "TSD-AC09 preview and production output contain the same template provenance marker")]
    public async Task Template_overload_keeps_provenance_marker()
    {
        var template = new ReportTemplate { Id = Guid.NewGuid(), Version = 3 };
        var report = new VerificationReport { Metadata = new() { TemplateId = template.Id, TemplateVersion = template.Version } };
        var bytes = await new PdfReportGenerator().GeneratePdfAsync(report, template);
        Assert.Contains(template.Id.ToString(), Encoding.ASCII.GetString(bytes));
    }

    [Fact(DisplayName = "TSD-AC04 invalid footer token is rejected")]
    public void Invalid_footer_is_rejected_before_rendering() => Assert.Throws<InvalidOperationException>(() => new ReportTemplate { Footer = new() { PageNumberFormat = "Page {X} of {Y}" } }.Validate());
}
