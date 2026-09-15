namespace DrawVerify.Core.Models;

public sealed record DocumentFingerprint
{
    public string Value { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public int CanonicalizationVersion { get; init; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value) && Value.Length == 64;
}

public sealed record DocumentFingerprintContext(
    Guid DocumentId,
    int Revision,
    Guid StepDocumentVersionId,
    Guid DxfDocumentVersionId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DocumentFingerprintArtifact(string RelativeName, Stream Content, long Length);

public enum FingerprintVerificationStatus { Match, Mismatch, Unavailable }

public sealed record FingerprintVerificationResult(FingerprintVerificationStatus Status, DocumentFingerprint? Computed, string Message)
{
    public bool IsMatch => Status == FingerprintVerificationStatus.Match;
}
