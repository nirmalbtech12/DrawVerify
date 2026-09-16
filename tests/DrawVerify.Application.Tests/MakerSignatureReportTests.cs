using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Pdf;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MakerSignatureReportTests
{
    [Fact(DisplayName = "TD-KAN64-REPORT-001 Markdown and PDF report retain Maker signature metadata")]
    public async Task Reports_include_signature_representation_and_metadata()
    {
        var signature = SignatureData.Create(SignatureMethod.Drawn, "stroke:1,2;3,4", "maker-001", "Maker One", Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero), "Engineering", "Design");
        var report = new VerificationReport { MakerSignature = signature };
        var markdown = await new MarkdownReportGenerator().GenerateMarkdownAsync(report);
        var pdf = await new PdfReportGenerator().GeneratePdfAsync(report);
        Assert.Contains("Maker One", markdown);
        Assert.Contains("Maker", markdown);
        Assert.Contains("2026-01-10", markdown);
        Assert.Contains("stroke:1,2;3,4", markdown);
        Assert.Contains("Maker One", System.Text.Encoding.ASCII.GetString(pdf));
        Assert.Contains("Drawn", System.Text.Encoding.ASCII.GetString(pdf));
    }

    [Fact(DisplayName = "TD-KAN64-REPORT-001 historical unsigned reports remain readable")]
    public async Task Historical_unsigned_report_is_not_misrepresented_as_signed()
    {
        var markdown = await new MarkdownReportGenerator().GenerateMarkdownAsync(new VerificationReport());
        Assert.DoesNotContain("Maker Signature", markdown);
        Assert.Contains("# Verification Report", markdown);
    }
}
