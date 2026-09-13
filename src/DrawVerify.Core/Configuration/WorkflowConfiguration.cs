using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Configuration;

/// <summary>
/// Deserialized workflow rules loaded from config/workflow.default.json.
/// Rules are data-driven and never hard-coded.
/// </summary>
public sealed class WorkflowConfiguration
{
    public IReadOnlyList<WorkflowStatus> PermittedInitialStates { get; init; } = Array.Empty<WorkflowStatus>();
    public WorkflowStatus SubmittedForReviewState { get; init; } = WorkflowStatus.SubmittedForReview;
    public IReadOnlyList<WorkflowStatus> DuplicateSubmissionStates { get; init; } = Array.Empty<WorkflowStatus>();
    public bool MarkChangedPackagesSuperseded { get; init; } = true;
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
    }
}
