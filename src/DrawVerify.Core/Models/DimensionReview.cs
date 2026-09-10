using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record DimensionReview
{
    public Guid DimensionMatchId { get; init; }
    public string? Comment { get; init; }
    public DimensionResolutionStatus Status { get; init; } = DimensionResolutionStatus.Open;
    public bool RequiresDesignUpdate { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
