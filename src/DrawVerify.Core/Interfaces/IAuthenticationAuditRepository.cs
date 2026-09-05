using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IAuthenticationAuditRepository
{
    Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default);
}
