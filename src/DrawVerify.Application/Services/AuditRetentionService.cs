using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
namespace DrawVerify.Application.Services;
public sealed class AuditRetentionService
{
    private readonly IAuditRetentionRepository _repository; private readonly ISessionAuthenticationService _session; private readonly RoleAuthorization _authorization;
    public AuditRetentionService(IAuditRetentionRepository repository, ISessionAuthenticationService session, AuditLogConfiguration configuration, RoleAuthorization? authorization = null) { configuration.Validate(); _repository = repository; _session = session; _authorization = authorization ?? new RoleAuthorization(); Configuration = configuration; }
    public AuditLogConfiguration Configuration { get; }
    public Task<int> ApplyAsync(CancellationToken cancellationToken = default) { if (!_authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration)) throw new UnauthorizedAccessException("Only a system-authorized administrator can apply retention."); return _repository.ApplyAsync(Configuration.RetentionDays, cancellationToken); }
}
