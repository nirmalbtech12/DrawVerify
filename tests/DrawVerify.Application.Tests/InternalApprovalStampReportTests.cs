using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampReportTests
{
    [Fact(DisplayName = "T12 exports carry stamp metadata and verification status")]
    public void T12_report_and_audit_exports_include_stamp_fields()
    {
        var report = Read("src", "DrawVerify.Core", "Models", "VerificationReport.cs");
        var audit = Read("src", "DrawVerify.Application", "Services", "AuditLogService.cs");
        Assert.Contains("InternalApprovalStamp", report);
        Assert.Contains("Stamp verification", report);
        foreach (var field in new[] { "StampId", "StampApproverDisplayName", "StampAppliedUtc", "StampFingerprintReference", "StampIntegrityHash" }) Assert.Contains(field, audit);
    }

    [Fact(DisplayName = "T17 compliance exports include stamp metadata and verification status")]
    public void T17_compliance_export_contract_includes_stamp_status()
    {
        var report = Read("src", "DrawVerify.Core", "Models", "VerificationReport.cs");
        var audit = Read("src", "DrawVerify.Application", "Services", "AuditLogService.cs");
        Assert.Contains("InternalApprovalStamp", report);
        Assert.Contains("VerificationStatus", report);
        Assert.Contains("ExportDocumentCsvAsync", audit);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
