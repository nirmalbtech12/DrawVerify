using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class WorkflowAction
{
    public WorkflowStatus From { get; init; }
    public WorkflowStatus To { get; init; }
    public string Name { get; init; } = string.Empty;
}
