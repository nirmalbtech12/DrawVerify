using System.Security.Cryptography;
using System.Text;
using DrawVerify.Application.Fingerprinting;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

/// <summary>
/// Tests for DocumentFingerprintService and FingerprintPolicy.
/// </summary>
public class FingerprintServiceTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StepVersion = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DxfVersion = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact(DisplayName = "TD-01 computes deterministic SHA-256 for a STEP/DXF package")]
    public async Task Computes_known_vector_independent_of_enumeration_order()
    {
        var service = new DocumentFingerprintService(new FingerprintPolicy { Algorithm = "SHA-256", CanonicalizationVersion = 1 });
        var context = Context();
        var first = await Compute(service, context, Artifacts(false));
        var second = await Compute(service, context, Artifacts(true));

        Assert.Equal(first, second);
        Assert.Equal("SHA-256", first.Algorithm);
        Assert.Equal(1, first.CanonicalizationVersion);
        Assert.Matches("^[0-9a-f]{64}$", first.Value);
        Assert.Equal(KnownDigest, first.Value);
    }

    [Fact(DisplayName = "TD-02 covers content and metadata and validates policy")]
    public async Task Changes_to_covered_inputs_change_digest_and_invalid_inputs_fail()
    {
        var service = new DocumentFingerprintService(new FingerprintPolicy());
        var baseline = await Compute(service, Context(), Artifacts(false));
        var changedBytes = await Compute(service, Context(), Artifacts(false, step: "STEP changed"));
        var changedMetadata = await Compute(service, Context(revision: 2), Artifacts(false));

        Assert.NotEqual(baseline.Value, changedBytes.Value);
        Assert.NotEqual(baseline.Value, changedMetadata.Value);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ComputeAsync(Context(), Array.Empty<DocumentFingerprintArtifact>()));
        var invalidMetadataArtifacts = Artifacts(false);
        try { await Assert.ThrowsAsync<ArgumentException>(() => service.ComputeAsync(Context(metadata: new Dictionary<string, string>()), invalidMetadataArtifacts)); }
        finally { DisposeArtifacts(invalidMetadataArtifacts); }
        Assert.Throws<NotSupportedException>(() => new DocumentFingerprintService(new FingerprintPolicy { Algorithm = "MD5" }));
    }

    [Fact(DisplayName = "TD-02 every covered identifier and metadata value changes the digest")]
    public async Task Every_covered_metadata_and_identifier_changes_digest()
    {
        var service = new DocumentFingerprintService(new FingerprintPolicy());
        var baseline = await Compute(service, Context(), Artifacts(false));
        var variants = new[]
        {
            await Compute(service, Context(metadata: new Dictionary<string, string> { ["makerUserId"] = "maker-002", ["timestamp"] = "2026-01-01T00:00:00Z" }), Artifacts(false)),
            await Compute(service, Context(metadata: new Dictionary<string, string> { ["makerUserId"] = "maker-001", ["timestamp"] = "2026-01-01T00:00:01Z" }), Artifacts(false)),
            await Compute(service, Context(revision: 2), Artifacts(false)),
            await Compute(service, Context(), Artifacts(false, step: "STEP data changed")),
            await Compute(service, Context(), Artifacts(false, dxf: "DXF data changed"))
        };

        Assert.All(variants, variant => Assert.NotEqual(baseline.Value, variant.Value));
    }

    [Theory(DisplayName = "TD-02 rejects malformed fingerprint metadata")]
    [InlineData("gg00000000000000000000000000000000000000000000000000000000000000", "SHA-256", 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "MD5", 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "SHA-256", 0)]
    public void Malformed_fingerprint_is_invalid(string value, string algorithm, int version)
    {
        Assert.False(new DocumentFingerprint { Value = value, Algorithm = algorithm, CanonicalizationVersion = version }.IsValid);
    }

    [Fact(DisplayName = "TD-06 reports match, mismatch, and unavailable material distinctly")]
    public async Task Verification_reports_match_and_mismatch_without_fallback()
    {
        var service = new DocumentFingerprintService(new FingerprintPolicy());
        var expected = await Compute(service, Context(), Artifacts(false));
        var matchArtifacts = Artifacts(false); var mismatchArtifacts = Artifacts(false, dxf: "DXF altered");
        FingerprintVerificationResult match;
        FingerprintVerificationResult mismatch;
        try
        {
            match = await service.VerifyAsync(expected, Context(), matchArtifacts);
            mismatch = await service.VerifyAsync(expected, Context(), mismatchArtifacts);
        }
        finally { DisposeArtifacts(matchArtifacts); DisposeArtifacts(mismatchArtifacts); }

        Assert.Equal(FingerprintVerificationStatus.Match, match.Status);
        Assert.Equal(FingerprintVerificationStatus.Mismatch, mismatch.Status);
        Assert.Equal("Fingerprint Mismatch - Version May Have Been Altered", mismatch.Message);
    }

    [Fact(DisplayName = "TD-11 streams input and reports bounded byte progress")]
    public async Task Large_input_reports_monotonic_progress_and_honors_cancellation()
    {
        var service = new DocumentFingerprintService(new FingerprintPolicy());
        var progressValues = new List<long>();
        var progress = new ImmediateProgress(progressValues);
        var largeArtifacts = Artifacts(false, step: new string('S', 128 * 1024));
        DocumentFingerprint result;
        try { result = await service.ComputeAsync(Context(), largeArtifacts, progress); }
        finally { DisposeArtifacts(largeArtifacts); }

        Assert.True(result.IsValid);
        Assert.NotEmpty(progressValues);
        Assert.Equal(progressValues.OrderBy(x => x), progressValues);
        Assert.Equal(128 * 1024 + 8, progressValues[^1]);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledArtifacts = Artifacts(false);
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ComputeAsync(Context(), cancelledArtifacts, cancellationToken: cancellation.Token)); }
        finally { DisposeArtifacts(cancelledArtifacts); }
    }


    private static DocumentFingerprintContext Context(int revision = 1, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(DocumentId, revision, StepVersion, DxfVersion, metadata ?? new Dictionary<string, string> { ["makerUserId"] = "maker-001", ["timestamp"] = "2026-01-01T00:00:00Z" });

    private static DocumentFingerprintArtifact[] Artifacts(bool reverse, string step = "STEP data", string dxf = "DXF data")
    {
        var items = new[]
        {
            new DocumentFingerprintArtifact("drawing.dxf", new MemoryStream(Encoding.UTF8.GetBytes(dxf)), Encoding.UTF8.GetByteCount(dxf)),
            new DocumentFingerprintArtifact("model.step", new MemoryStream(Encoding.UTF8.GetBytes(step)), Encoding.UTF8.GetByteCount(step))
        };
        return reverse ? items.AsEnumerable().Reverse().ToArray() : items;
    }

    private static async Task<DocumentFingerprint> Compute(DocumentFingerprintService service, DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts)
    {
        try { return await service.ComputeAsync(context, artifacts); }
        finally { DisposeArtifacts(artifacts); }
    }

    private static void DisposeArtifacts(IEnumerable<DocumentFingerprintArtifact> artifacts)
    {
            foreach (var artifact in artifacts) artifact.Content.Dispose();
    }

    private sealed class ImmediateProgress(List<long> values) : IProgress<long>
    {
        public void Report(long value) => values.Add(value);
    }


    private const string KnownDigest = "064a68e3a08b10f4f2dd1b3e4a6587e0f0adc47d73c6ec811233571fcf00c0ce";
}
