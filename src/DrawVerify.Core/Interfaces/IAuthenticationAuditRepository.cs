using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IAuthenticationAuditRepository
{
    Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default);
    Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(new AuditLogPage(Array.Empty<AuthenticationAuditEntry>(), query.Page, query.PageSize, 0));
}
