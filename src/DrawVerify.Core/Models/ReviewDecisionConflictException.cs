using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class ReviewDecisionConflictException : InvalidOperationException
{
    public ReviewDecisionConflictException(Guid packageId, int requestedRevision, WorkflowStatus currentStatus, int currentRevision, ReviewDecision? priorDecision)
        : base(BuildMessage(packageId, requestedRevision, currentStatus, currentRevision, priorDecision))
    {
        PackageId = packageId;
        RequestedRevision = requestedRevision;
        CurrentStatus = currentStatus;
        CurrentRevision = currentRevision;
        PriorDecision = priorDecision;
    }

    public Guid PackageId { get; }
    public int RequestedRevision { get; }
    public WorkflowStatus CurrentStatus { get; }
    public int CurrentRevision { get; }
    public ReviewDecision? PriorDecision { get; }

    private static string BuildMessage(Guid packageId, int requestedRevision, WorkflowStatus status, int revision, ReviewDecision? prior)
        => prior is null
            ? $"The review decision was not saved for package '{packageId}' revision {requestedRevision}. Current package status is '{status}' revision {revision}; refresh before deciding."
            : $"The review decision was not saved for package '{packageId}' revision {requestedRevision}. Current package status is '{status}' revision {revision}; prior decision is '{prior.DecisionType}' by {prior.CheckerDisplayName} at {prior.DecidedUtc:O}.";
}
