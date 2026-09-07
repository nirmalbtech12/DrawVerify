using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
namespace DrawVerify.Application.Services;
public sealed class AuditRetentionService
{
    private readonly IAuditRetentionRepository _repository;
    public AuditRetentionService(IAuditRetentionRepository repository, AuditLogConfiguration configuration) { configuration.Validate(); _repository = repository; Configuration = configuration; }
    public AuditLogConfiguration Configuration { get; }
    internal Task<int> ApplyAsync(CancellationToken cancellationToken = default) => _repository.ApplyAsync(Configuration.RetentionDays, cancellationToken);
}
