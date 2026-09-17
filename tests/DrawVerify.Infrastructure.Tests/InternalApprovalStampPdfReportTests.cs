using System.Text;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Pdf;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class InternalApprovalStampPdfReportTests
{
    [Fact(DisplayName = "TSD-AC06-SIGNATURE-ACTIONS renders internal stamp identity and status")]
    public async Task TSD_AC06_SIGNATURE_ACTIONS_renders_stamp_action_fields()
    {
        var stamp = InternalApprovalStamp.Create(Guid.NewGuid(), 1, "approver", "Approver One", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "fingerprint", Guid.NewGuid());
        var content = Encoding.ASCII.GetString(await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { InternalApprovalStamp = stamp }));
        Assert.Contains(stamp.ApproverDisplayName, content); Assert.Contains(stamp.StampId.ToString(), content); Assert.Contains(stamp.VerificationStatus.ToString(), content);
    }

    [Fact(DisplayName = "T11 PDF/report rendering preserves stamp representation and status")]
    public async Task T11_pdf_contains_stamp_representation_and_metadata()
    {
        var stamp = InternalApprovalStamp.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, "approver-001", "Approver One", new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero), "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var pdf = await new PdfReportGenerator().GeneratePdfAsync(new VerificationReport { InternalApprovalStamp = stamp });
        var content = Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", content, StringComparison.Ordinal);
        Assert.Contains("Internal Approval Stamp", content);
        Assert.Contains(stamp.StampId.ToString(), content);
        Assert.Contains(stamp.ApproverDisplayName, content);
        Assert.Contains(stamp.FingerprintReference, content);
        Assert.Contains(stamp.VerificationStatus.ToString(), content);
        Assert.EndsWith("%%EOF\n", content, StringComparison.Ordinal);
    }
}
