using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record PriorVersionFilter
{
    public WorkflowStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? MakerUserId { get; init; }

    public void Validate()
    {
        if (FromUtc.HasValue && ToUtc.HasValue && FromUtc.Value > ToUtc.Value)
            throw new ArgumentException("The prior-version date range is invalid.");
        if (FromUtc.HasValue && FromUtc.Value.Kind == DateTimeKind.Local || ToUtc.HasValue && ToUtc.Value.Kind == DateTimeKind.Local)
            throw new ArgumentException("Prior-version filter dates must be UTC.");
    }
}
