using DrawVerify.Core.Enums;
namespace DrawVerify.Core.Models;
public sealed record WorkflowStatusTransition
{
    public WorkflowStatus From { get; init; }
    public WorkflowStatus To { get; init; }
    public IReadOnlyList<string> AuthorizedRoles { get; init; } = Array.Empty<string>();
}
