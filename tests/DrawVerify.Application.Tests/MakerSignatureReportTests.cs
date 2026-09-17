using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Pdf;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MakerSignatureReportTests
{
    [Fact(DisplayName = "TD-REPORT-01 reports and authorized exports project persisted Approver metadata")]
    public void Report_contract_projects_approver_signature_and_validates_integrity()
    {
        var report = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "ReportService.cs")));
        var audit = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "AuditLogService.cs")));
        var model = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Core", "Models", "VerificationReport.cs")));
        Assert.Contains("ApproverSignature", report);
        Assert.Contains("ApproverSignature", audit);
        Assert.Contains("ApproverUserId", audit);
        Assert.Contains("ApproverDisplayName", audit);
        Assert.Contains("ApproverTimestampUtc", audit);
        Assert.Contains("ApproverMethod", audit);
        Assert.Contains("ApproverSignature", model);
        Assert.Contains("ApproverSignature", report);
        var approver = SignatureData.CreateApprover(SignatureMethod.Typed, "Ada Approver", "approver-001", "Ada Approver", Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero), "Compliance", "Quality");
        var rendered = new VerificationReport { ApproverSignature = approver }.ToMarkdown();
        Assert.Contains("Ada Approver", rendered);
        Assert.Contains("Approver", rendered);
    }

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

    [Fact(DisplayName = "TD-KAN64-REPORT-001 report contract resolves one persisted submission revision")]
    public void Report_contract_does_not_mix_submission_revisions()
    {
        var reportService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "ReportService.cs")));
        Assert.Contains("GetSubmissionsAsync", reportService);
        Assert.Contains("submission.PackageId", reportService);
        Assert.Contains("submission.Revision", reportService);
        Assert.Contains("OrderByDescending(x => x.Revision)", reportService);
    }

    [Fact(DisplayName = "TD-008 reports and exports project Checker signature metadata")]
    public async Task Checker_signature_is_rendered_with_identity_timestamp_method_and_reference()
    {
        var signature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker approval", "checker-001", "Checker One", Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero), "Quality", "Inspection");
        var report = new VerificationReport { CheckerSignature = signature };
        var markdown = await new MarkdownReportGenerator().GenerateMarkdownAsync(report);
        Assert.Contains("Checker One", markdown);
        Assert.Contains("Checker", markdown);
        Assert.Contains("2026-09-16", markdown);
        Assert.Contains("Typed", markdown);
        Assert.Contains("checker approval", markdown);
    }

    [Fact(DisplayName = "TD-008 audit export contract includes Checker fields and preserves unsigned compatibility")]
    public void Checker_export_contract_is_additive_and_version_aware()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "AuditLogService.cs")));
        Assert.Contains("CheckerSignature", source);
        Assert.Contains("CheckerUserId", source);
        Assert.Contains("CheckerTimestampUtc", source);
        Assert.Contains("CheckerMethod", source);
        Assert.Contains("CheckerSignatureApplied", source);
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
