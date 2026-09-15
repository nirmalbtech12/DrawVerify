using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Fingerprinting;

/// <summary>
/// Computes and compares document fingerprints per FingerprintPolicy.
/// </summary>
public sealed class DocumentFingerprintService : IDocumentFingerprintService
{
    private readonly FingerprintPolicy _policy;
    public DocumentFingerprintService(FingerprintPolicy policy) { _policy = policy ?? throw new ArgumentNullException(nameof(policy)); _policy.Validate(); }

    public async Task<DocumentFingerprint> ComputeAsync(DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (context.DocumentId == Guid.Empty || context.StepDocumentVersionId == Guid.Empty || context.DxfDocumentVersionId == Guid.Empty) throw new ArgumentException("Document and version identifiers are required.", nameof(context));
        if (artifacts is null || artifacts.Count == 0) throw new ArgumentException("At least one artifact is required.", nameof(artifacts));
        if (artifacts.Any(x => string.IsNullOrWhiteSpace(x.RelativeName) || x.Content is null || x.Length < 0)) throw new ArgumentException("Every artifact must have a name, readable content, and non-negative length.", nameof(artifacts));
        if (context.Metadata is null || context.Metadata.Count == 0 || context.Metadata.Any(x => string.IsNullOrWhiteSpace(x.Key))) throw new ArgumentException("Required fingerprint metadata is missing.", nameof(context));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var metadata = new StringBuilder().Append("fingerprint-manifest\n").Append("canonicalization=").Append(_policy.CanonicalizationVersion).Append('\n').Append("document=").Append(context.DocumentId.ToString("D")).Append('\n').Append("revision=").Append(context.Revision).Append('\n').Append("step-version=").Append(context.StepDocumentVersionId.ToString("D")).Append('\n').Append("dxf-version=").Append(context.DxfDocumentVersionId.ToString("D")).Append('\n');
        foreach (var item in context.Metadata.OrderBy(x => x.Key, StringComparer.Ordinal)) metadata.Append("meta:").Append(item.Key).Append('=').Append(item.Value ?? string.Empty).Append('\n');
        Append(hash, Encoding.UTF8.GetBytes(metadata.ToString()));
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long processed = 0;
            foreach (var artifact in artifacts.OrderBy(x => x.RelativeName.Trim().Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RelativeName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(hash, Encoding.UTF8.GetBytes($"artifact={artifact.RelativeName.Trim().Replace('\\', '/')};length={artifact.Length}\n"));
                int read;
                while ((read = await artifact.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read); processed += read; progress?.Report(processed);
                }
            }
            return new DocumentFingerprint { Value = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), Algorithm = "SHA-256", CanonicalizationVersion = _policy.CanonicalizationVersion };
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    private static void Append(IncrementalHash hash, byte[] data) => hash.AppendData(data);

    public async Task<FingerprintVerificationResult> VerifyAsync(DocumentFingerprint expected, DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var computed = await ComputeAsync(context, artifacts, cancellationToken: cancellationToken).ConfigureAwait(false);
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected.Value.ToLowerInvariant()), Encoding.ASCII.GetBytes(computed.Value));
        return new FingerprintVerificationResult(matches ? FingerprintVerificationStatus.Match : FingerprintVerificationStatus.Mismatch, computed, matches ? "Fingerprint matches the submitted version." : "Fingerprint Mismatch - Version May Have Been Altered");
    }
}
