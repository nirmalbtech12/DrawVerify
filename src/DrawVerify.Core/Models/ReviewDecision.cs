using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record ReviewDecision
{
    public Guid DecisionId { get; init; } = Guid.NewGuid();
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string CheckerUserId { get; init; } = string.Empty;
    public string CheckerDisplayName { get; init; } = string.Empty;
    public DateTime DecidedUtc { get; init; } = DateTime.UtcNow;
    public ReviewDecisionType DecisionType { get; init; }
    public string? Comments { get; init; }
    public string SnapshotReference { get; init; } = string.Empty;
    public string SnapshotJson { get; init; } = "{}";
    public string ApproverUserId { get; init; } = string.Empty;
    public string ApproverDisplayName { get; init; } = string.Empty;
    public bool IsFinalApproval { get; init; }
    public string? OverallReason { get; init; }
    public string? CorrectionAction { get; init; }
    public Guid? PriorDecisionId { get; init; }
    public ReviewDecisionRoute Route { get; init; }
    public string? DestinationRole { get; init; }
    public string? DestinationQueue { get; init; }

    public void Validate() => Validate(requireCorrectionAction: false);

    public void Validate(bool requireCorrectionAction)
    {
        if (PackageId == Guid.Empty) throw new ArgumentException("Package identifier is required.", nameof(PackageId));
        if (Revision < 1) throw new ArgumentOutOfRangeException(nameof(Revision));
        if (string.IsNullOrWhiteSpace(CheckerUserId) && string.IsNullOrWhiteSpace(ApproverUserId)) throw new ArgumentException("Decision identity is required.", nameof(CheckerUserId));
        if (DecisionType is (ReviewDecisionType.Reject or ReviewDecisionType.RequestChanges or ReviewDecisionType.Return) && string.IsNullOrWhiteSpace(Comments))
            throw new ArgumentException("Comments are required for rejection or requested changes.", nameof(Comments));
        if (DecisionType is (ReviewDecisionType.Reject or ReviewDecisionType.RequestChanges or ReviewDecisionType.Return) && string.IsNullOrWhiteSpace(OverallReason ?? Comments))
            throw new ArgumentException("An overall rejection reason is required.", nameof(OverallReason));
        if (requireCorrectionAction && DecisionType is (ReviewDecisionType.Reject or ReviewDecisionType.RequestChanges or ReviewDecisionType.Return) && string.IsNullOrWhiteSpace(CorrectionAction))
            throw new ArgumentException("A correction action is required for rejection or requested changes.", nameof(CorrectionAction));
        if (IsFinalApproval && Route != ReviewDecisionRoute.None && DecisionType is (ReviewDecisionType.Reject or ReviewDecisionType.Return))
        {
            if (Route != ReviewDecisionRoute.ToChecker) throw new ArgumentException("Final rejection must be routed to the Checker.", nameof(Route));
            if (!string.Equals(DestinationRole, "Checker", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Final rejection destination must be the Checker.", nameof(DestinationRole));
        }
    }
}
