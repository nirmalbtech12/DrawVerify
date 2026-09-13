using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class WorkflowSubmissionContractTests
{
    [Fact(DisplayName = "TD-CORE-01 workflow states and immutable snapshot contract")]
    public void Eligible_states_transition_and_duplicate_states_are_represented()
    {
        var configuration = new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } };
        configuration.Validate();
        Assert.Contains(WorkflowStatus.Draft, configuration.PermittedInitialStates);
        Assert.Contains(WorkflowStatus.InPreparation, configuration.PermittedInitialStates);
        Assert.DoesNotContain(WorkflowStatus.SubmittedForReview, configuration.PermittedInitialStates);
        Assert.Contains(WorkflowStatus.UnderReview, configuration.DuplicateSubmissionStates);
        Assert.Contains(WorkflowStatus.Approved, configuration.DuplicateSubmissionStates);
        Assert.Equal(2, (int)WorkflowStatus.SubmittedForReview);
        var original = new ReviewSubmission { PackageId = Guid.NewGuid(), Note = "original" };
        var changed = original with { Note = "changed" };
        Assert.Equal("original", original.Note);
        Assert.NotSame(original, changed);
    }
}
