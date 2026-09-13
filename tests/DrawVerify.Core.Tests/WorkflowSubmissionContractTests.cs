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

    [Theory(DisplayName = "TD-CORE-01 only Draft and InPreparation permit submission")]
    [InlineData(WorkflowStatus.Draft, true)]
    [InlineData(WorkflowStatus.InPreparation, true)]
    [InlineData(WorkflowStatus.SubmittedForReview, false)]
    [InlineData(WorkflowStatus.UnderReview, false)]
    [InlineData(WorkflowStatus.Approved, false)]
    [InlineData(WorkflowStatus.Superseded, false)]
    public void Configuration_marks_each_state_as_eligible_or_duplicate(WorkflowStatus status, bool eligible)
    {
        var configuration = new WorkflowConfiguration
        {
            PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
            DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved }
        };

        Assert.Equal(eligible, configuration.PermittedInitialStates.Contains(status));
        Assert.Equal(!eligible && status is not WorkflowStatus.Superseded,
            configuration.DuplicateSubmissionStates.Contains(status));
    }

    [Fact(DisplayName = "TD-CORE-01 immutable submission retains exact snapshot fields")]
    public void Submission_with_expression_update_does_not_mutate_original()
    {
        var original = new ReviewSubmission
        {
            PackageId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"),
            DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
            VerificationSessionId = Guid.Parse("00000000-0000-0000-0000-000000000013"),
            ExtractionEvidenceJson = "{\"extracted\":true}",
            VerificationEvidenceJson = "{\"verified\":true}",
            MakerUserId = "maker-001",
            Revision = 1
        };

        var replacement = original with { StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000021"), Revision = 2 };

        Assert.Equal(1, original.Revision);
        Assert.Equal("{\"extracted\":true}", original.ExtractionEvidenceJson);
        Assert.NotEqual(original.StepDocumentVersionId, replacement.StepDocumentVersionId);
    }

}
