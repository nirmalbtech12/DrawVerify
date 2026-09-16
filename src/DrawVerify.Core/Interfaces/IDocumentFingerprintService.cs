using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IDocumentFingerprintService
{
    Task<DocumentFingerprint> ComputeAsync(
        DocumentFingerprintContext context,
        IReadOnlyCollection<DocumentFingerprintArtifact> artifacts,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FingerprintVerificationResult> VerifyAsync(
        DocumentFingerprint expected,
        DocumentFingerprintContext context,
        IReadOnlyCollection<DocumentFingerprintArtifact> artifacts,
        CancellationToken cancellationToken = default);
}
