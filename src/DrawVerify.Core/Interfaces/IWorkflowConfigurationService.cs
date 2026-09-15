using DrawVerify.Core.Configuration;
using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IWorkflowConfigurationService
{
    Task<WorkflowConfiguration> GetAsync(CancellationToken cancellationToken = default);
    Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, int? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<WorkflowPreviewResult> PreviewAsync(WorkflowConfiguration configuration, WorkflowPreviewMetadata metadata, CancellationToken cancellationToken = default);
}
