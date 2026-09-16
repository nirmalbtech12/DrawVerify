using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class MakerSignatureContractTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset SignedAt = new(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly string MinimalPngPayload = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    [Fact(DisplayName = "TD-KAN64-CORE-001 supported methods retain canonical metadata")]
    public void Supported_methods_are_finalized_and_canonical()
    {
        foreach (var method in new[] { SignatureMethod.Typed, SignatureMethod.Drawn, SignatureMethod.Uploaded })
        {
            var payload = method switch
            {
                SignatureMethod.Drawn => "stroke:1,2;3,4",
                SignatureMethod.Uploaded => MinimalPngPayload,
                _ => "Maker One"
            };
            var signature = SignatureData.Create(method, payload, "maker-001", "Maker One", Document, 1, SignedAt, "Engineering", "Design");
            signature.Validate();
            Assert.True(signature.IsTamperEvident);
            Assert.Contains("maker-001", signature.CanonicalRepresentation);
            Assert.Equal(signature.CanonicalRepresentation, (signature with { }).CanonicalRepresentation);
            Assert.Equal(signature.PayloadHash, SignatureData.ComputePayloadHash(signature.Payload));
        }
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 uploaded payload validation rejects corrupt and unsupported data")]
    public void Uploaded_payload_validation_rejects_invalid_image_data()
    {
        Assert.Throws<ArgumentException>(() => SignatureData.ValidateUploadedPayload(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not an image"))));
        Assert.Throws<ArgumentException>(() => SignatureData.ValidateUploadedPayload(string.Empty));
        Assert.Throws<ArgumentException>(() => SignatureData.ValidateUploadedPayload(Convert.ToBase64String(new byte[] { 0x00, 0x01, 0x02 })));
        Assert.Throws<ArgumentException>(() => SignatureData.ValidateUploadedPayload(Convert.ToBase64String(new byte[5 * 1024 * 1024 + 1])));
        SignatureData.ValidateUploadedPayload(MinimalPngPayload);
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 invalid or unauthorized signatures are rejected")]
    public void Invalid_signature_inputs_are_rejected()
    {
        var valid = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Document, 1, SignedAt);
        Assert.Throws<ArgumentException>(() => (valid with { Payload = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { Method = (SignatureMethod)999 }).Validate());
        Assert.Throws<UnauthorizedAccessException>(() => (valid with { Role = SystemRoles.Checker }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { DocumentId = Guid.Empty }).Validate());
        Assert.Throws<InvalidOperationException>(() => (valid with { PayloadHash = "bad" }).Validate());
        Assert.Throws<InvalidOperationException>(() => (valid with { IsFinalized = false }).Validate());
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 finalized signature snapshots cannot be changed in place")]
    public void Finalized_snapshot_remains_unchanged_after_expression_updates()
    {
        var original = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Document, 1, SignedAt);
        var changed = original with { Payload = "Altered", IsFinalized = false };
        Assert.Equal("Maker One", original.Payload);
        Assert.True(original.IsFinalized);
        Assert.NotEqual(original.Payload, changed.Payload);
        Assert.Throws<InvalidOperationException>(() => changed.Validate());
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 revisions keep distinct signature associations")]
    public void Resubmission_uses_a_distinct_revision_signature()
    {
        var rejected = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Document, 1, SignedAt);
        var resubmitted = SignatureData.Create(SignatureMethod.Drawn, "stroke:1,2;3,4", "maker-001", "Maker One", Document, 2, SignedAt.AddMinutes(5));
        Assert.Equal(1, rejected.Revision);
        Assert.Equal(2, resubmitted.Revision);
        Assert.NotEqual(rejected.PayloadHash, resubmitted.PayloadHash);
        Assert.Equal(1, new ReviewSubmission { MakerSignature = rejected, Revision = 1 }.MakerSignature!.Revision);
        Assert.Equal(2, new ReviewSubmission { MakerSignature = resubmitted, Revision = 2 }.MakerSignature!.Revision);
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 audit action is additive and serializable")]
    public void Maker_signature_audit_action_serializes_without_changing_existing_values()
    {
        Assert.Equal(65, (int)DocumentAuditAction.MakerSignatureApplied);
        Assert.Equal(32, (int)DocumentAuditAction.SubmissionSucceeded);
        var json = JsonSerializer.Serialize(DocumentAuditAction.MakerSignatureApplied);
        Assert.Equal("65", json);
    }

    [Fact(DisplayName = "TD-KAN64-CORE-001 legacy unsigned records remain readable")]
    public void Legacy_unsigned_submission_and_report_remain_compatible()
    {
        var submission = JsonSerializer.Deserialize<ReviewSubmission>(JsonSerializer.Serialize(new ReviewSubmission { PackageId = Document, Revision = 1 }));
        var report = JsonSerializer.Deserialize<VerificationReport>(JsonSerializer.Serialize(new VerificationReport()));
        Assert.NotNull(submission);
        Assert.Null(submission!.MakerSignature);
        Assert.NotNull(report);
        Assert.Null(report!.MakerSignature);
    }

    [Fact(DisplayName = "TD-001 Checker signatures enforce metadata, UTC, methods, digest, immutability, and versioning")]
    public void Checker_signature_contract_is_version_scoped_and_tamper_evident()
    {
        foreach (var method in new[] { SignatureMethod.Typed, SignatureMethod.Drawn, SignatureMethod.Uploaded, SignatureMethod.StoredProfile })
        {
            var payload = method == SignatureMethod.Uploaded ? MinimalPngPayload : method == SignatureMethod.Drawn ? "stroke:1,2;3,4" : "checker-approval";
            var signature = SignatureData.CreateChecker(method, payload, "checker-001", "Checker One", Document, 1, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero), "Quality", "Inspection");
            signature.Validate();
            Assert.Equal(SystemRoles.Checker, signature.Role);
            Assert.Equal("checker-001", signature.CheckerUserId);
            Assert.Equal(TimeSpan.Zero, signature.TimestampUtc.Offset);
            Assert.True(signature.IsTamperEvident);
            Assert.Throws<InvalidOperationException>(() => (signature with { IntegrityHash = "tampered" }).Validate());
        }

        var v1 = SignatureData.CreateChecker(SignatureMethod.Typed, "v1", "checker-001", "Checker One", Document, 1, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
        var v2 = SignatureData.CreateChecker(SignatureMethod.Typed, "v2", "checker-001", "Checker One", Document, 2, new DateTimeOffset(2026, 9, 16, 10, 1, 0, TimeSpan.Zero));
        Assert.NotEqual(v1.Revision, v2.Revision);
        Assert.NotEqual(v1.IntegrityHash, v2.IntegrityHash);
        Assert.Throws<ArgumentException>(() => (v1 with { CheckerUserId = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (v1 with { TimestampUtc = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(1)) }).Validate());
    }

    [Fact(DisplayName = "TD-001 Checker audit action and approval sequencing contracts are additive")]
    public void Checker_audit_and_sequence_contracts_are_present()
    {
        Assert.Equal(66, (int)DocumentAuditAction.CheckerSignatureApplied);
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("Maker signature required before Checker signature", workflow);
        Assert.Contains("Checker signature required before approval", workflow);
        Assert.Contains("ReviewDecisionType.Approve", workflow);
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
