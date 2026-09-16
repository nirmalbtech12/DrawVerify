namespace DrawVerify.Infrastructure.Signatures;

using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Renders visible signature/stamp imagery onto reports and drawings.
/// </summary>
public sealed class SignatureStampService : ISignatureService
{
    public SignatureData Finalize(SignatureData signature) { signature.Validate(); return signature; }
    public string Canonicalize(SignatureData signature) { signature.Validate(); return signature.CanonicalRepresentation; }
    public bool VerifyIntegrity(SignatureData signature) => signature.IsTamperEvident;
    public string Preview(SignatureData signature) { signature.Validate(requireFinalized: false); return signature.Payload; }
}
