using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Configuration;

/// <summary>
/// Deserialized workflow rules loaded from config/workflow.default.json.
/// Rules are data-driven and never hard-coded.
/// </summary>
public sealed record WorkflowConfiguration
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<Models.WorkflowRoleDefinition> Roles { get; init; } = Array.Empty<Models.WorkflowRoleDefinition>();
    public IReadOnlyList<Models.WorkflowStageDefinition> Stages { get; init; } = Array.Empty<Models.WorkflowStageDefinition>();
    public IReadOnlyList<Models.WorkflowTriggerDefinition> Triggers { get; init; } = Array.Empty<Models.WorkflowTriggerDefinition>();
    public IReadOnlyList<Models.WorkflowNotificationRule> Notifications { get; init; } = Array.Empty<Models.WorkflowNotificationRule>();
    public Models.WorkflowRevisionPolicy RevisionPolicy { get; init; } = new();
    public WorkflowRolloutPolicy RolloutPolicy { get; init; } = WorkflowRolloutPolicy.CaptureAtWorkflowStart;
    public IReadOnlyDictionary<WorkflowStatus, string> StatusLabels { get; init; } = new Dictionary<WorkflowStatus, string>();
    public IReadOnlyDictionary<WorkflowStatus, int> StatusProgress { get; init; } = new Dictionary<WorkflowStatus, int>();
    public IReadOnlyList<Models.WorkflowStatusTransition> StatusTransitions { get; init; } = Array.Empty<Models.WorkflowStatusTransition>();
    public IReadOnlyList<WorkflowStatus> PermittedInitialStates { get; init; } = Array.Empty<WorkflowStatus>();
    public WorkflowStatus SubmittedForReviewState { get; init; } = WorkflowStatus.SubmittedForReview;
    public IReadOnlyList<WorkflowStatus> DuplicateSubmissionStates { get; init; } = Array.Empty<WorkflowStatus>();
    public bool MarkChangedPackagesSuperseded { get; init; } = true;
    public WorkflowStatus ApprovedReviewState { get; init; } = WorkflowStatus.Approved;
    public WorkflowStatus RejectedReviewState { get; init; } = WorkflowStatus.Rejected;
    public WorkflowStatus ChangesRequestedReviewState { get; init; } = WorkflowStatus.ChangesRequested;
    public void Validate()
    {
        if (PermittedInitialStates.Count == 0) throw new InvalidOperationException("Workflow configuration must define permitted initial states.");
        if (!PermittedInitialStates.Contains(WorkflowStatus.Draft) || !PermittedInitialStates.Contains(WorkflowStatus.InPreparation)) throw new InvalidOperationException("Workflow configuration must permit Draft and InPreparation submissions.");
        if (SubmittedForReviewState != WorkflowStatus.SubmittedForReview) throw new InvalidOperationException("Workflow target must be SubmittedForReview.");
        if (!DuplicateSubmissionStates.Contains(WorkflowStatus.SubmittedForReview) || !DuplicateSubmissionStates.Contains(WorkflowStatus.UnderReview) || !DuplicateSubmissionStates.Contains(WorkflowStatus.Approved)) throw new InvalidOperationException("Workflow configuration must define all duplicate submission states.");
        if (PermittedInitialStates.Distinct().Count() != PermittedInitialStates.Count)
            throw new InvalidOperationException("Workflow configuration must not repeat permitted initial states.");
        if (DuplicateSubmissionStates.Distinct().Count() != DuplicateSubmissionStates.Count)
            throw new InvalidOperationException("Workflow configuration must not repeat duplicate submission states.");
        if (PermittedInitialStates.Intersect(DuplicateSubmissionStates).Any())
            throw new InvalidOperationException("Workflow configuration cannot permit a duplicate-submission state as an initial state.");
        if (PermittedInitialStates.Contains(SubmittedForReviewState))
            throw new InvalidOperationException("Workflow configuration cannot permit its submission target as an initial state.");
        foreach (var label in StatusLabels) if (string.IsNullOrWhiteSpace(label.Value)) throw new InvalidOperationException($"Workflow status label for '{label.Key}' cannot be empty.");
        foreach (var progress in StatusProgress) if (progress.Value < 0) throw new InvalidOperationException($"Workflow status progress for '{progress.Key}' cannot be negative.");
        var errors = ApplicationValidation();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }
    public IReadOnlyList<string> ApplicationValidation()
    {
        var errors = new List<string>();
        if (Version < 1) errors.Add("Version must be at least 1.");
        var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in Roles)
        {
            if (string.IsNullOrWhiteSpace(role.DisplayName)) errors.Add("Roles.DisplayName is required.");
            if (!roleNames.Add(role.DisplayName)) errors.Add($"Roles.DisplayName '{role.DisplayName}' is duplicated.");
            if (!Authorization.SystemRoles.Catalog.Any(x => string.Equals(x.Id, role.SystemRole, StringComparison.OrdinalIgnoreCase))) errors.Add($"Roles.{role.DisplayName} references undefined system role '{role.SystemRole}'.");
        }
        var known = roleNames.Count == 0 ? Authorization.SystemRoles.Catalog.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) : roleNames;
        foreach (var stage in Stages)
        {
            if (stage.RequiredApprovals < 1 || stage.RequiredApprovals > stage.Roles.Count) errors.Add($"Stages.{stage.Name}.RequiredApprovals must be between 1 and the assigned role count.");
            foreach (var role in stage.Roles) if (!known.Contains(role)) errors.Add($"Stages.{stage.Name} references undefined role '{role}'.");
        }
        foreach (var transition in StatusTransitions)
            foreach (var role in transition.AuthorizedRoles) if (!known.Contains(role)) errors.Add($"StatusTransitions references undefined role '{role}'.");
        foreach (var notification in Notifications)
        {
            foreach (var role in notification.RecipientRoles) if (!known.Contains(role)) errors.Add($"Notifications.{notification.Event} references undefined role '{role}'.");
            if (notification.Channels.Count == 0) errors.Add($"Notifications.{notification.Event}.Channels must define at least one supported channel.");
            if (notification.Channels.Any(channel => channel != WorkflowNotificationChannel.InApp)) errors.Add($"Notifications.{notification.Event}.Channels contains an unsupported channel.");
        }
        return errors;
    }
}
