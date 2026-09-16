namespace DrawVerify.Infrastructure.Signatures;

using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Renders visible signature/stamp imagery onto reports and drawings.
/// </summary>
public sealed class SignatureStampService : ISignatureService
{
    public SignatureData Finalize(SignatureData signature) { Validate(signature); return signature; }
    public string Canonicalize(SignatureData signature) { Validate(signature); return signature.CanonicalRepresentation; }
    public bool VerifyIntegrity(SignatureData signature) => signature.IsTamperEvident;
    public string Preview(SignatureData signature) { Validate(signature, requireFinalized: false); return signature.Payload; }
    private static void Validate(SignatureData signature, bool requireFinalized = true)
    {
        signature.Validate(requireFinalized);
        if (signature.Method == DrawVerify.Core.Enums.SignatureMethod.Uploaded) SignatureData.ValidateUploadedPayload(signature.Payload);
    }
}
