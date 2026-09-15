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
    public WorkflowStatus ApprovedState => _configuration.ApprovedReviewState;
    public WorkflowStatus RejectedState => _configuration.RejectedReviewState;
    public WorkflowStatus ChangesRequestedState => _configuration.ChangesRequestedReviewState;
    public SubmissionReadiness ValidateState(WorkflowStatus status) => CanSubmit(status) ? SubmissionReadiness.Ready : new(false, new[] { $"The package is in '{status}', and cannot be submitted for review." });
    public WorkflowState Submit(WorkflowState state)
    {
        if (!CanSubmit(state.Status)) throw new InvalidOperationException($"The package is in '{state.Status}', and cannot be submitted for review.");
        if (_configuration.StatusTransitions.Count > 0 && !_configuration.StatusTransitions.Any(x => x.From == state.Status && x.To == _configuration.SubmittedForReviewState))
            throw new InvalidOperationException($"The configured workflow does not permit '{state.Status}' to transition to '{_configuration.SubmittedForReviewState}'.");
        return state with { Status = _configuration.SubmittedForReviewState, UpdatedUtc = DateTime.UtcNow };
    }

    public bool MatchesTrigger(WorkflowPreviewMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return _configuration.Triggers.Count == 0 || _configuration.Triggers.Any(trigger =>
            (trigger.DocumentTypes.Count == 0 || trigger.DocumentTypes.Contains(metadata.DocumentType ?? string.Empty, StringComparer.OrdinalIgnoreCase)) &&
            (trigger.Projects.Count == 0 || trigger.Projects.Contains(metadata.Project ?? string.Empty, StringComparer.OrdinalIgnoreCase)) &&
            (trigger.Departments.Count == 0 || trigger.Departments.Contains(metadata.Department ?? string.Empty, StringComparer.OrdinalIgnoreCase)) &&
            (!trigger.MinimumRevision.HasValue || metadata.Revision >= trigger.MinimumRevision) &&
            (!trigger.RiskLevel.HasValue || (metadata.RiskLevel ?? 0) >= trigger.RiskLevel) &&
            (!trigger.MinimumValue.HasValue || (metadata.Value ?? 0) >= trigger.MinimumValue));
    }

    public bool CanTransition(WorkflowStatus from, WorkflowStatus to, string canonicalOrDisplayRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalOrDisplayRole);
        return _configuration.StatusTransitions.Any(transition => transition.From == from && transition.To == to && transition.AuthorizedRoles.Contains(canonicalOrDisplayRole, StringComparer.OrdinalIgnoreCase));
    }

    public bool HasRequiredApprovals(string stageName, int approvals)
    {
        var stage = _configuration.Stages.FirstOrDefault(x => string.Equals(x.Name, stageName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Configured workflow stage '{stageName}' was not found.");
        return approvals >= stage.RequiredApprovals;
    }

    public bool CanDelegate(string stageName) => GetStage(stageName).AllowDelegation;
    public bool CanSubstitute(string stageName) => GetStage(stageName).AllowSubstitution;
    public string? RejectionRoute(string stageName, bool hasComment)
    {
        var stage = GetStage(stageName);
        if (stage.RequireRejectionComment && !hasComment) throw new InvalidOperationException($"Stage '{stageName}' requires a rejection comment.");
        return stage.RejectTo;
    }

    public IReadOnlyList<string> NotificationRecipients(WorkflowNotificationEvent notificationEvent) =>
        _configuration.Notifications.Where(x => x.Event == notificationEvent).SelectMany(x => x.RecipientRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public bool CreatesRevisionOnSubmission => _configuration.RevisionPolicy.CreateRevisionOnSubmission;
    public bool RetainsRejectedRevisions => _configuration.RevisionPolicy.RetainRejectedRevisions;
    public WorkflowRolloutPolicy RolloutPolicy => _configuration.RolloutPolicy;

    private WorkflowStageDefinition GetStage(string stageName) =>
        _configuration.Stages.FirstOrDefault(x => string.Equals(x.Name, stageName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Configured workflow stage '{stageName}' was not found.");
}
