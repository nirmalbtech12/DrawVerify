namespace DrawVerify.Core.Models;

public sealed record DocumentFingerprint
{
    public string Value { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public int CanonicalizationVersion { get; init; }
    public bool IsValid => Value.Length == 64
        && Value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
        && string.Equals(Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase)
        && CanonicalizationVersion > 0;
}

public sealed record DocumentFingerprintContext(
    Guid DocumentId,
    int Revision,
    Guid StepDocumentVersionId,
    Guid DxfDocumentVersionId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DocumentFingerprintArtifact(string RelativeName, Stream Content, long Length);

public enum FingerprintVerificationStatus { Match, Mismatch, Error, Unavailable }

public sealed record FingerprintVerificationResult(FingerprintVerificationStatus Status, DocumentFingerprint? Computed, string Message)
{
    public DocumentFingerprint? Stored { get; init; }
    public DateTimeOffset? VerifiedUtc { get; init; }
    public string? VerifierUserId { get; init; }
    public string? VerifierDisplayName { get; init; }
    public DateTime? SubmissionTimestampUtc { get; init; }
    public string? MakerUserId { get; init; }
    public string? MakerDisplayName { get; init; }
    public Guid DocumentId { get; init; }
    public int Revision { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public string? ErrorDetails { get; init; }
    public bool IsIntegrityFailure => Status == FingerprintVerificationStatus.Mismatch;
    public bool IsMatch => Status == FingerprintVerificationStatus.Match;
}
