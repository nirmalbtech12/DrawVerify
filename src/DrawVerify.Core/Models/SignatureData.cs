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
    public string CheckerUserId { get; init; } = string.Empty;
    public string CheckerDisplayName { get; init; } = string.Empty;
    public string ApproverUserId { get; init; } = string.Empty;
    public string ApproverDisplayName { get; init; } = string.Empty;
    public string IntegrityHash { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public int Revision { get; init; }
    public bool IsFinalized { get; init; }
    public string CanonicalRepresentation => string.Join("|", Method, Payload, SignerUserId, SignerDisplayName, Role, TimestampUtc.UtcDateTime.ToString("O"), Title ?? "", Department ?? "", DocumentId, Revision);
    public bool IsChecker => string.Equals(Role, SystemRoles.Checker, StringComparison.OrdinalIgnoreCase);
    public bool IsApprover => string.Equals(Role, SystemRoles.Approver, StringComparison.OrdinalIgnoreCase);
    public string SignerUserId => IsChecker ? CheckerUserId : IsApprover ? ApproverUserId : MakerUserId;
    public string SignerDisplayName => IsChecker ? CheckerDisplayName : IsApprover ? ApproverDisplayName : MakerDisplayName;
    public bool IsTamperEvident => IsFinalized && ((IsChecker || IsApprover) ? string.Equals(IntegrityHash, ComputePayloadHash(CanonicalRepresentation), StringComparison.OrdinalIgnoreCase) : string.Equals(PayloadHash, ComputePayloadHash(Payload), StringComparison.OrdinalIgnoreCase));
    public void Validate(bool requireFinalized = true)
    {
        if (!Enum.IsDefined(Method)) throw new ArgumentException("Unsupported signature method.", nameof(Method));
        if (string.IsNullOrWhiteSpace(Payload)) throw new ArgumentException("Signature payload is required.", nameof(Payload));
        if (Method == SignatureMethod.Uploaded) ValidateUploadedPayload(Payload);
        if (IsChecker || IsApprover)
        {
            if (IsChecker && string.IsNullOrWhiteSpace(CheckerUserId) && !string.IsNullOrWhiteSpace(MakerUserId)) throw new UnauthorizedAccessException("Only a Checker can apply a Checker signature.");
            if (string.IsNullOrWhiteSpace(SignerUserId) || string.IsNullOrWhiteSpace(SignerDisplayName)) throw new ArgumentException($"{Role} identity is required.", nameof(SignerUserId));
            if (string.IsNullOrWhiteSpace(IntegrityHash)) throw new ArgumentException($"{Role} signature integrity hash is required.", nameof(IntegrityHash));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(MakerUserId) || string.IsNullOrWhiteSpace(MakerDisplayName)) throw new ArgumentException("Maker identity is required.", nameof(MakerUserId));
            if (!string.Equals(Role, SystemRoles.Maker, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Only a Maker can apply a Maker signature.");
        }
        if (DocumentId == Guid.Empty || Revision <= 0) throw new ArgumentException("Document and revision identity are required.");
        if (TimestampUtc == default || TimestampUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Signature timestamp must be UTC.");
        if (requireFinalized && !IsFinalized) throw new InvalidOperationException("The signature must be finalized before submission.");
        if (requireFinalized && !IsTamperEvident) throw new InvalidOperationException("The signature integrity hash is invalid.");
    }
    public static void ValidateUploadedPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Uploaded signature payload is required.", nameof(payload));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(payload); }
        catch (FormatException exception) { throw new ArgumentException("Uploaded signature payload is not valid image data.", nameof(payload), exception); }
        if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) throw new ArgumentException("Uploaded signature image is empty or exceeds the supported size.", nameof(payload));
        var supported = IsPng(bytes) || IsJpeg(bytes) || IsBmp(bytes);
        if (!supported) throw new ArgumentException("Uploaded signature payload is not a supported PNG, JPEG, or BMP image.", nameof(payload));
    }
    private static bool IsPng(byte[] bytes) => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    private static bool IsJpeg(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
    private static bool IsBmp(byte[] bytes) => bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
    public static SignatureData Create(SignatureMethod method, string payload, string makerUserId, string makerDisplayName, Guid documentId, int revision, DateTimeOffset timestampUtc, string? title = null, string? department = null)
    {
        var value = new SignatureData { Method = method, Payload = payload.Trim(), PayloadHash = ComputePayloadHash(payload.Trim()), MakerUserId = makerUserId.Trim(), MakerDisplayName = makerDisplayName.Trim(), TimestampUtc = timestampUtc.ToUniversalTime(), Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(), DocumentId = documentId, Revision = revision, IsFinalized = true };
        value.Validate(); return value;
    }
    public static SignatureData CreateChecker(SignatureMethod method, string payload, string checkerUserId, string checkerDisplayName, Guid documentId, int revision, DateTimeOffset timestampUtc, string? title = null, string? department = null)
    {
        var value = new SignatureData { Method = method, Payload = payload.Trim(), CheckerUserId = checkerUserId.Trim(), CheckerDisplayName = checkerDisplayName.Trim(), Role = SystemRoles.Checker, TimestampUtc = timestampUtc.ToUniversalTime(), Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(), DocumentId = documentId, Revision = revision, IsFinalized = true };
        value = value with { IntegrityHash = ComputePayloadHash(value.CanonicalRepresentation) };
        value.Validate(); return value;
    }
    public static SignatureData CreateApprover(SignatureMethod method, string payload, string approverUserId, string approverDisplayName, Guid documentId, int revision, DateTimeOffset timestampUtc, string? title = null, string? department = null)
    {
        var value = new SignatureData { Method = method, Payload = payload.Trim(), ApproverUserId = approverUserId.Trim(), ApproverDisplayName = approverDisplayName.Trim(), Role = SystemRoles.Approver, TimestampUtc = timestampUtc.ToUniversalTime(), Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(), DocumentId = documentId, Revision = revision, IsFinalized = true };
        value = value with { IntegrityHash = ComputePayloadHash(value.CanonicalRepresentation) };
        value.Validate(); return value;
    }
    public static string ComputePayloadHash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}
