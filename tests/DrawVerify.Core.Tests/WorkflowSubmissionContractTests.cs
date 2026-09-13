using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Text.Json;
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

    [Fact(DisplayName = "TD-CORE-01 workflow configuration rejects contradictory rules")]
    public void Configuration_requires_the_submitted_state_and_all_duplicate_states()
    {
        var configurations = new[]
        {
            new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview } },
            new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft }, SubmittedForReviewState = WorkflowStatus.UnderReview, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }
        };

        foreach (var configuration in configurations)
            Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Fact(DisplayName = "TD-CORE-01 preserves legacy audit action values")]
    public void Existing_audit_action_values_remain_stable()
    {
        Assert.Equal(1, (int)DocumentAuditAction.Uploaded);
        Assert.Equal(4, (int)DocumentAuditAction.Cancelled);
        Assert.Equal(19, (int)DocumentAuditAction.StatusChanged);
        Assert.Equal(32, (int)DocumentAuditAction.SubmissionSucceeded);
        Assert.Equal(33, (int)DocumentAuditAction.SubmissionCancelled);
        Assert.Equal(34, (int)DocumentAuditAction.SubmissionRejected);
    }

    [Fact(DisplayName = "TD-CORE-049 review contracts validate, serialize, and preserve snapshots")]
    public void Review_contracts_validate_required_fields_and_round_trip_exact_evidence()
    {
        var packageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var step = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var dxf = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var submission = new ReviewSubmission
        {
            PackageId = packageId, StepDocumentVersionId = step, DxfDocumentVersionId = dxf,
            ExtractionEvidenceJson = "{\"dimensions\":[10]}", VerificationEvidenceJson = "{\"missing\":[\"D1\"]}",
            MakerUserId = "maker-1", MakerDisplayName = "Maker One", Revision = 7,
            SubmittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var package = ReviewPackage.FromSubmission(submission);
        var decision = new ReviewDecision
        {
            PackageId = packageId, Revision = 7, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One",
            DecisionType = ReviewDecisionType.Approve, SnapshotReference = package.SnapshotReference,
            SnapshotJson = JsonSerializer.Serialize(package)
        };

        decision.Validate();
        var roundTrip = JsonSerializer.Deserialize<ReviewDecision>(JsonSerializer.Serialize(decision));
        Assert.NotNull(roundTrip);
        Assert.Equal(decision, roundTrip);
        Assert.Equal(step, package.StepDocumentVersionId);
        Assert.Equal(dxf, package.DxfDocumentVersionId);
        Assert.Equal(submission.ExtractionEvidenceJson, package.ExtractionEvidenceJson);
        Assert.Equal(submission.VerificationEvidenceJson, package.VerificationEvidenceJson);
        Assert.Throws<ArgumentException>(() => (decision with { DecisionType = ReviewDecisionType.Reject, Comments = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (decision with { DecisionType = ReviewDecisionType.RequestChanges, Comments = null }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (decision with { Revision = 0 }).Validate());
        Assert.Equal(36, (int)DocumentAuditAction.ReviewStarted);
        Assert.Equal(37, (int)DocumentAuditAction.Approved);
        Assert.Equal(38, (int)DocumentAuditAction.Rejected);
        Assert.Equal(39, (int)DocumentAuditAction.ChangesRequested);
    }

}
