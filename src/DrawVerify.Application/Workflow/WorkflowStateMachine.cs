using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Workflow;

public sealed class WorkflowStateMachine
{
    private readonly WorkflowConfiguration _configuration;
    public WorkflowStateMachine(WorkflowConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
    }
    public bool CanSubmit(WorkflowStatus status) => _configuration.PermittedInitialStates.Contains(status);
    public SubmissionReadiness ValidateState(WorkflowStatus status) => CanSubmit(status) ? SubmissionReadiness.Ready : new(false, new[] { $"The package is in '{status}', and cannot be submitted for review." });
    public WorkflowState Submit(WorkflowState state)
    {
        if (!CanSubmit(state.Status)) throw new InvalidOperationException($"The package is in '{state.Status}', and cannot be submitted for review.");
        return state with { Status = _configuration.SubmittedForReviewState, UpdatedUtc = DateTime.UtcNow };
    }
}
