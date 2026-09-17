using System.Security.Cryptography;
using System.Text;
using DrawVerify.Core.Authorization;

namespace DrawVerify.Core.Models;

public enum InternalApprovalStampVerificationStatus { Valid, Invalid }

public sealed record InternalApprovalStamp
{
    public Guid StampId { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string ApproverUserId { get; init; } = string.Empty;
    public string ApproverDisplayName { get; init; } = string.Empty;
    public string ApproverRole { get; init; } = SystemRoles.Approver;
    public DateTimeOffset AppliedUtc { get; init; }
    public string FingerprintReference { get; init; } = string.Empty;
    public InternalApprovalStampVerificationStatus VerificationStatus { get; init; } = InternalApprovalStampVerificationStatus.Valid;
    public string? OrganizationName { get; init; }
    public string? ApprovalText { get; init; } = "Internally Approved";
    public string Placement { get; init; } = "Approval";
    public string CanonicalRepresentation => string.Concat(
        CanonicalField(StampId.ToString("D")), CanonicalField(DocumentId.ToString("D")), CanonicalField(PackageId.ToString("D")),
        CanonicalField(Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), CanonicalField(ApproverUserId),
        CanonicalField(ApproverDisplayName), CanonicalField(ApproverRole), CanonicalField(AppliedUtc.UtcDateTime.ToString("O")),
        CanonicalField(FingerprintReference), CanonicalField(OrganizationName), CanonicalField(ApprovalText), CanonicalField(Placement));
    public string IntegrityHash { get; init; } = string.Empty;
    public void Validate()
    {
        if (StampId == Guid.Empty || DocumentId == Guid.Empty || PackageId == Guid.Empty || Revision <= 0) throw new ArgumentException("Stamp document and revision identity are required.");
        if (string.IsNullOrWhiteSpace(ApproverUserId) || string.IsNullOrWhiteSpace(ApproverDisplayName)) throw new ArgumentException("Approver identity is required.");
        if (!string.Equals(ApproverRole, SystemRoles.Approver, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Only an Approver can apply an internal approval stamp.");
        if (AppliedUtc == default || AppliedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Stamp timestamp must be UTC.");
        if (string.IsNullOrWhiteSpace(FingerprintReference)) throw new ArgumentException("Stamp fingerprint reference is required.");
        if (string.IsNullOrWhiteSpace(IntegrityHash) || !string.Equals(IntegrityHash, ComputeIntegrityHash(CanonicalRepresentation), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Stamp integrity hash is invalid.");
        if (!Enum.IsDefined(VerificationStatus)) throw new ArgumentException("Stamp verification status is invalid.");
        if (string.IsNullOrWhiteSpace(Placement)) throw new ArgumentException("Stamp placement is required.");
    }
    public static InternalApprovalStamp Create(Guid documentId, int revision, string approverUserId, string approverDisplayName, DateTimeOffset appliedUtc, string fingerprintReference, Guid? stampId = null, string? organizationName = null, string? approvalText = "Internally Approved", string placement = "Approval")
    {
        if (documentId == Guid.Empty || revision <= 0) throw new ArgumentException("Stamp document and revision identity are required.");
        if (string.IsNullOrWhiteSpace(approverUserId) || string.IsNullOrWhiteSpace(approverDisplayName)) throw new ArgumentException("Approver identity is required.");
        if (appliedUtc == default || appliedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Stamp timestamp must be UTC.");
        if (string.IsNullOrWhiteSpace(fingerprintReference)) throw new ArgumentException("Stamp fingerprint reference is required.");
        if (string.IsNullOrWhiteSpace(placement)) throw new ArgumentException("Stamp placement is required.");
        var stamp = new InternalApprovalStamp { StampId = stampId ?? Guid.NewGuid(), DocumentId = documentId, PackageId = documentId, Revision = revision, ApproverUserId = approverUserId.Trim(), ApproverDisplayName = approverDisplayName.Trim(), AppliedUtc = appliedUtc.ToUniversalTime(), FingerprintReference = fingerprintReference.Trim(), OrganizationName = string.IsNullOrWhiteSpace(organizationName) ? null : organizationName.Trim(), ApprovalText = string.IsNullOrWhiteSpace(approvalText) ? "Internally Approved" : approvalText.Trim(), Placement = placement.Trim() };
        stamp = stamp with { IntegrityHash = ComputeIntegrityHash(stamp.CanonicalRepresentation) };
        stamp.Validate();
        return stamp;
    }
    private static string CanonicalField(string? value) => value is null ? "-1:" : $"{value.Length}:{value}";
    private static string ComputeIntegrityHash(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
