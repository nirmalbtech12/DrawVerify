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
            Tables = [new ReportTemplateTable { Key = "Dimensions", Columns = [new() { Field = "DimensionId", Label = "Identifier", Precision = 2, Unit = "mm" }, new() { Field = "Deviation", Label = "Delta", Precision = 1 }] }],
            Summary = new() { Layout = "KeyMetrics", Statistics = [new() { Key = "TotalDimensions", Label = "Total", Format = "0" }] },
            Style = new() { Orientation = "Landscape", Margin = 30, BodyFontSize = 11, HeaderColor = "#123456" }
        };
        var bytes = await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { Metadata = new() { DocumentName = "sample", TemplateId = template.Id, TemplateVersion = template.Version }, AuditTrail = [new() { Details = "sample audit" }] }, template);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("xref\n", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/MediaBox [0 0 792 612]", text);
        Assert.Contains("(# Configured title) Tj", text);
        Assert.Contains("(Confidential | v7 | Effective date: 2026-01-02) Tj", text);
        Assert.Contains("(Page 1 of 1) Tj", text);
        Assert.Contains("BT /F1 11 Tf 30 762 Td", text);
        Assert.Contains("(Table: Dimensions | Columns: Identifier (mm) | Delta) Tj", text);
        Assert.Contains("(Total: 0) Tj", text);
    }

    [Fact(DisplayName = "TSD-AC09 preview and production output contain the same template provenance marker")]
    public async Task Template_overload_keeps_provenance_marker()
    {
        var template = new ReportTemplate { Id = Guid.NewGuid(), Version = 3 };
        var report = new VerificationReport { Metadata = new() { TemplateId = template.Id, TemplateVersion = template.Version } };
        var bytes = await new PdfReportGenerator().GeneratePdfAsync(report, template);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains($"(Template ID: {template.Id} | Template Version: 3) Tj", text);
        Assert.Contains("/MediaBox [0 0 612 792]", text);
    }

    [Fact(DisplayName = "TSD-AC04 invalid footer token is rejected")]
    public void Invalid_footer_is_rejected_before_rendering() => Assert.Throws<InvalidOperationException>(() => new ReportTemplate { Footer = new() { PageNumberFormat = "Page {X} of {Y}" } }.Validate());
}
