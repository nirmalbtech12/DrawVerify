using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

/// <summary>
/// Applies signature/stamp operations on reports via ISignatureService.
/// </summary>
public sealed class SignatureService
{
    private readonly ISessionAuthenticationService _authentication;
    public SignatureService(ISessionAuthenticationService authentication) => _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    public SignatureData Finalize(SignatureData signature)
    {
        if (_authentication.CurrentUser is not { } user || !user.HasRole(SystemRoles.Maker)) throw new UnauthorizedAccessException("Only a Maker can apply a Maker signature.");
        if (!string.Equals(user.UserId, signature.MakerUserId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("The authenticated Maker does not match the signature identity.");
        signature.Validate(); return signature;
    }
}
