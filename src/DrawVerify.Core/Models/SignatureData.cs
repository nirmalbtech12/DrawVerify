using System.Security.Cryptography;
using System.Text;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record SignatureData
{
    public SignatureMethod Method { get; init; }
    public string Payload { get; init; } = string.Empty;
    public string PayloadHash { get; init; } = string.Empty;
    public string MakerUserId { get; init; } = string.Empty;
    public string MakerDisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = SystemRoles.Maker;
    public DateTimeOffset TimestampUtc { get; init; }
    public string? Title { get; init; }
    public string? Department { get; init; }
    public Guid DocumentId { get; init; }
    public int Revision { get; init; }
    public bool IsFinalized { get; init; }
    public string CanonicalRepresentation => string.Join("|", Method, Payload, MakerUserId, MakerDisplayName, Role, TimestampUtc.UtcDateTime.ToString("O"), Title ?? "", Department ?? "", DocumentId, Revision);
    public bool IsTamperEvident => IsFinalized && string.Equals(PayloadHash, ComputePayloadHash(Payload), StringComparison.OrdinalIgnoreCase);
    public void Validate(bool requireFinalized = true)
    {
        if (!Enum.IsDefined(Method)) throw new ArgumentException("Unsupported signature method.", nameof(Method));
        if (string.IsNullOrWhiteSpace(Payload)) throw new ArgumentException("Signature payload is required.", nameof(Payload));
        if (string.IsNullOrWhiteSpace(MakerUserId) || string.IsNullOrWhiteSpace(MakerDisplayName)) throw new ArgumentException("Maker identity is required.", nameof(MakerUserId));
        if (!string.Equals(Role, SystemRoles.Maker, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Only a Maker can apply a Maker signature.");
        if (DocumentId == Guid.Empty || Revision <= 0) throw new ArgumentException("Document and revision identity are required.");
        if (TimestampUtc == default || TimestampUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Signature timestamp must be UTC.");
        if (requireFinalized && !IsFinalized) throw new InvalidOperationException("The signature must be finalized before submission.");
        if (requireFinalized && !IsTamperEvident) throw new InvalidOperationException("The signature integrity hash is invalid.");
    }
    public static SignatureData Create(SignatureMethod method, string payload, string makerUserId, string makerDisplayName, Guid documentId, int revision, DateTimeOffset timestampUtc, string? title = null, string? department = null)
    {
        var value = new SignatureData { Method = method, Payload = payload.Trim(), PayloadHash = ComputePayloadHash(payload.Trim()), MakerUserId = makerUserId.Trim(), MakerDisplayName = makerDisplayName.Trim(), TimestampUtc = timestampUtc.ToUniversalTime(), Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(), DocumentId = documentId, Revision = revision, IsFinalized = true };
        value.Validate(); return value;
    }
    public static string ComputePayloadHash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}
