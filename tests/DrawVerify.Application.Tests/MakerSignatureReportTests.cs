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

    [Fact(DisplayName = "TD-KAN64-REPORT-001 report contract includes persisted revision and authorized export paths")]
    public void Report_contract_declares_revision_resolution_authorization_and_signature_export()
    {
        var reportService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "ReportService.cs")));
        var auditService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "AuditLogService.cs")));
        Assert.Contains("MakerSignature", reportService);
        Assert.Contains("Revision", reportService);
        Assert.Contains("MakerSignature", auditService);
        Assert.Contains("ExportDocumentCsvAsync", auditService);
        Assert.Contains("UnauthorizedAccessException", auditService);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
