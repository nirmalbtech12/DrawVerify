using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class InternalApprovalStampTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T01 complete internal approval stamp validates all required metadata")]
    public void T01_complete_stamp_contains_required_metadata()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "sha256-document-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        stamp.Validate();
        Assert.Equal(Document, stamp.DocumentId);
        Assert.Equal(1, stamp.Revision);
        Assert.Equal("Approver One", stamp.ApproverDisplayName);
        Assert.Equal("Approver", stamp.ApproverRole);
        Assert.Equal("sha256-document-001", stamp.FingerprintReference);
        Assert.Equal(InternalApprovalStampVerificationStatus.Valid, stamp.VerificationStatus);
        Assert.Contains("approver-001", stamp.CanonicalRepresentation);
        foreach (var invalid in new[]
        {
            stamp with { ApproverUserId = "" }, stamp with { ApproverDisplayName = "" },
            stamp with { ApproverRole = "Checker" }, stamp with { DocumentId = Guid.Empty },
            stamp with { Revision = 0 }, stamp with { AppliedUtc = default },
            stamp with { StampId = Guid.Empty }, stamp with { FingerprintReference = "" }
        }) Assert.ThrowsAny<Exception>(() => invalid.Validate());

        Assert.Throws<ArgumentException>(() => InternalApprovalStamp.Create(Document, 1, null!, "Approver One", Applied, "fingerprint"));
        Assert.Throws<ArgumentException>(() => InternalApprovalStamp.Create(Document, 1, "approver-001", null!, Applied, "fingerprint"));
        Assert.Throws<ArgumentException>(() => InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, null!));
    }

    [Fact(DisplayName = "T02 canonicalization is deterministic and finalized snapshots are immutable")]
    public void T02_canonicalization_and_immutability_are_tamper_evident()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint");
        var altered = stamp with { ApproverDisplayName = "Altered" };
        Assert.Equal(stamp.CanonicalRepresentation, (stamp with { }).CanonicalRepresentation);
        Assert.NotEqual(stamp.CanonicalRepresentation, altered.CanonicalRepresentation);
        Assert.Equal("Approver One", stamp.ApproverDisplayName);
        Assert.Throws<ArgumentException>(() => altered.Validate());
        Assert.Equal(stamp.IntegrityHash, JsonSerializer.Deserialize<InternalApprovalStamp>(JsonSerializer.Serialize(stamp))!.IntegrityHash);

        var delimiterLeft = InternalApprovalStamp.Create(Document, 1, "approver|001", "Approver One", Applied, "finger|print");
        var delimiterRight = InternalApprovalStamp.Create(Document, 1, "approver", "001", Applied, "finger|print");
        Assert.NotEqual(delimiterLeft.CanonicalRepresentation, delimiterRight.CanonicalRepresentation);
        Assert.NotEqual(delimiterLeft.IntegrityHash, delimiterRight.IntegrityHash);
    }

    [Fact(DisplayName = "T18 legacy unstamped submission and report remain compatible")]
    public void T18_legacy_payloads_keep_null_stamp()
    {
        var submission = JsonSerializer.Deserialize<ReviewSubmission>(JsonSerializer.Serialize(new ReviewSubmission { PackageId = Document, Revision = 1 }));
        var report = JsonSerializer.Deserialize<VerificationReport>(JsonSerializer.Serialize(new VerificationReport()));
        Assert.NotNull(submission);
        Assert.Null(submission!.InternalApprovalStamp);
        Assert.NotNull(report);
        Assert.Null(report!.InternalApprovalStamp);
        Assert.Equal(68, (int)DocumentAuditAction.InternalApprovalStampApplied);
    }
}
