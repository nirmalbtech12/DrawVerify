using DrawVerify.Core.Configuration;
using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IWorkflowConfigurationRepository
{
    Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default);
    Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken cancellationToken = default);
}
