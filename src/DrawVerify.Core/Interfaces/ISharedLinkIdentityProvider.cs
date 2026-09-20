using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface ISharedLinkIdentityProvider
{
    bool IsAuthenticated { get; }
    bool IsInternal(AuthenticatedUser user);
    bool IsAllowed(AuthenticatedUser user, InternalSharedLink link);
}
