using DrawVerify.Core.Enums;
namespace DrawVerify.Core.Models;
public sealed record WorkflowStageDefinition
{
    public string Name { get; init; } = string.Empty;
    public WorkflowApprovalMode Mode { get; init; } = WorkflowApprovalMode.Sequential;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public int RequiredApprovals { get; init; } = 1;
    public bool AllowDelegation { get; init; }
    public bool AllowSubstitution { get; init; }
    public string? RejectTo { get; init; }
    public bool RequireRejectionComment { get; init; }
}
