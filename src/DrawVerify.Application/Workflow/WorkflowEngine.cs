using DrawVerify.Core.Models;

namespace DrawVerify.Application.Workflow;

public sealed class WorkflowEngine
{
    private readonly WorkflowStateMachine _machine;
    public WorkflowEngine(WorkflowStateMachine machine) => _machine = machine ?? throw new ArgumentNullException(nameof(machine));
    public WorkflowState Submit(WorkflowState state) => _machine.Submit(state);
}
