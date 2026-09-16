namespace DrawVerify.Core.Interfaces;

using DrawVerify.Core.Models;

/// <summary>
/// Applies digital signatures / stamps to documents and reports.
/// </summary>
public interface ISignatureService
{
    SignatureData Finalize(SignatureData signature);
    string Canonicalize(SignatureData signature);
    bool VerifyIntegrity(SignatureData signature);
    string Preview(SignatureData signature);
}
