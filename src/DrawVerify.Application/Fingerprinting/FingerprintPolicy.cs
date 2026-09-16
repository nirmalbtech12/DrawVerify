using System.Text.Json;

namespace DrawVerify.Application.Fingerprinting;

/// <summary>
/// Defines which files are fingerprinted and at which lifecycle stages,
/// loaded from config/fingerprint-policy.default.json.
/// </summary>
public sealed class FingerprintPolicy
{
    public int Version { get; init; } = 1;
    public string Algorithm { get; init; } = "SHA-256";
    public int CanonicalizationVersion { get; init; } = 1;

    public void Validate()
    {
        if (Version <= 0 || CanonicalizationVersion <= 0) throw new InvalidOperationException("Fingerprint policy versions must be positive.");
        if (!string.Equals(Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException($"Fingerprint algorithm '{Algorithm}' is not supported.");
    }

    public static FingerprintPolicy FromFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement.TryGetProperty("fingerprintPolicy", out var configured) ? configured : document.RootElement;
        var policy = new FingerprintPolicy
        {
            Version = root.TryGetProperty("version", out var version) ? version.GetInt32() : 1,
            Algorithm = root.TryGetProperty("algorithm", out var algorithm) ? algorithm.GetString() ?? string.Empty : string.Empty,
            CanonicalizationVersion = root.TryGetProperty("canonicalizationVersion", out var canonicalization) ? canonicalization.GetInt32() : 1
        };
        policy.Validate();
        return policy;
    }
}
