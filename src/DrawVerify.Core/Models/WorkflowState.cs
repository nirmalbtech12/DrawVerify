using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record WorkflowState
{
    public Guid PackageId { get; init; }
    public WorkflowStatus Status { get; init; } = WorkflowStatus.Draft;
    public int Revision { get; init; } = 1;
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}
