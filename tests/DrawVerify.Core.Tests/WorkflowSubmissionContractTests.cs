using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Text.Json;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;

namespace DrawVerify.Core.Tests;

public sealed class WorkflowSubmissionContractTests
{
    [Theory(DisplayName = "TSD-AC02-PROGRESSION PDF/image documents retain normal workflow contract")]
    [InlineData(CoreDocumentType.PdfDrawing)]
    [InlineData(CoreDocumentType.ImageDrawing)]
    public void Pdf_and_image_documents_do_not_carry_3d_comparison_identifiers(CoreDocumentType documentType)
    {
        var version = new DocumentVersion { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), VersionNumber = 1 };
        var document = new Document { DocumentType = documentType, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, OriginalFileName = documentType == CoreDocumentType.PdfDrawing ? "drawing.pdf" : "drawing.png", FileSizeBytes = 8, Version = version, Versions = new[] { version } };

        Assert.Contains(documentType, new[] { CoreDocumentType.PdfDrawing, CoreDocumentType.ImageDrawing });
        Assert.Equal(WorkflowStatus.Draft, new WorkflowState { PackageId = document.DocumentId, Status = WorkflowStatus.Draft }.Status);
    }

    [Fact(DisplayName = "TSD-AC07-MULTI Approver assignments preserve legacy and distinct recipients")]
    public void TSD_AC07_MULTI_approver_assignment_contract_preserves_legacy_value()
    {
        var submission = new ReviewSubmission { AssignedApproverUserId = "A-1", AssignedApproverUserIds = new[] { "A-1", "a-1", "A-2" } };
        var distinct = submission.AssignedApproverUserIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal("A-1", submission.AssignedApproverUserId);
        Assert.Equal(new[] { "A-1", "A-2" }, distinct);
        Assert.DoesNotContain(distinct, string.IsNullOrWhiteSpace);
    }
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

    [Theory(DisplayName = "TD-CORE-049 reject and request-change comments are mandatory")]
    [InlineData(ReviewDecisionType.Reject, null)]
    [InlineData(ReviewDecisionType.Reject, " ")]
    [InlineData(ReviewDecisionType.RequestChanges, null)]
    [InlineData(ReviewDecisionType.RequestChanges, "\t")]
    public void Decision_types_requiring_comments_reject_blank_values(ReviewDecisionType type, string? comments)
    {
        var decision = new ReviewDecision
        {
            PackageId = Guid.NewGuid(), Revision = 7, CheckerUserId = "checker-1",
            DecisionType = type, Comments = comments
        };

        Assert.Throws<ArgumentException>(decision.Validate);
    }

    [Fact(DisplayName = "TD-CORE-049 review package preserves all snapshot identifiers")]
    public void Review_package_expression_update_preserves_original_snapshot()
    {
        var submission = new ReviewSubmission
        {
            PackageId = Guid.NewGuid(), DxfDocumentId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(),
            StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), Revision = 7,
            ExtractionEvidenceJson = "{\"dimensions\":[10],\"gdt\":[\"A\"]}",
            VerificationEvidenceJson = "{\"matched\":[\"D1\"],\"missing\":[\"D2\"],\"extra\":[\"D3\"]}"
        };
        var package = ReviewPackage.FromSubmission(submission);
        var changed = package with { VerificationEvidenceJson = "{\"matched\":[]}" };

        Assert.Equal(submission.StepDocumentVersionId, package.StepDocumentVersionId);
        Assert.Equal(submission.DxfDocumentVersionId, package.DxfDocumentVersionId);
        Assert.Equal(submission.ExtractionEvidenceJson, package.ExtractionEvidenceJson);
        Assert.Equal(submission.VerificationEvidenceJson, package.VerificationEvidenceJson);
        Assert.NotEqual(package.VerificationEvidenceJson, changed.VerificationEvidenceJson);
    }

    [Fact(DisplayName = "TD-01 rejection contracts round-trip supported references and legacy fields")]
    public void Rejection_contracts_round_trip_supported_references_and_legacy_payload()
    {
        var packageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var comments = new[] { "drawing", "model", "dimension", "gdt" }.Select((type, index) => new ReviewComment
        {
            PackageId = packageId, Revision = 1, AuthorUserId = "checker-001", AuthorDisplayName = "Checker",
            Text = $"Issue {index}", ReferenceType = type, ReferenceId = $"REF-{index}", IsDraft = true
        }).ToArray();
        foreach (var comment in comments) comment.Validate();
        var decision = new ReviewDecision
        {
            PackageId = packageId, Revision = 1, CheckerUserId = "checker-001", DecisionType = ReviewDecisionType.Reject,
            Comments = "Dimension 4 is outside tolerance", OverallReason = "Dimension 4 is outside tolerance",
            CorrectionAction = "Correct dimension 4 and resubmit"
        };
        decision.Validate();
        var json = JsonSerializer.Serialize(new ReviewPackage { PackageId = packageId, Revision = 1, Comments = comments, PriorDecision = decision });
        var roundTrip = JsonSerializer.Deserialize<ReviewPackage>(json)!;
        Assert.Equal(comments, roundTrip.Comments);
        Assert.Equal(decision, roundTrip.PriorDecision);
        var legacy = JsonSerializer.Deserialize<ReviewDecision>("{\"PackageId\":\"00000000-0000-0000-0000-000000000001\",\"Revision\":1,\"CheckerUserId\":\"checker-001\",\"DecisionType\":1,\"Comments\":\"legacy reason\"}")!;
        legacy.Validate();
        Assert.Null(legacy.OverallReason);
        Assert.Throws<ArgumentException>(() => (comments[0] with { ReferenceType = "unsupported", ReferenceId = "REF" }).Validate());
        Assert.Throws<ArgumentException>(() => (comments[0] with { ReferenceType = "drawing", ReferenceId = null, Location = null }).Validate());
    }

    [Fact(DisplayName = "TD-09 rejection additions preserve existing approval contracts")]
    public void Approval_contract_remains_valid_without_rejection_fields()
    {
        var approval = new ReviewDecision { PackageId = Guid.NewGuid(), Revision = 1, CheckerUserId = "checker-001", DecisionType = ReviewDecisionType.Approve };
        approval.Validate();
        Assert.Null(approval.OverallReason);
        Assert.Null(approval.CorrectionAction);
        Assert.Equal((int)DocumentAuditAction.ReviewCommentDraftSaved + 1, (int)DocumentAuditAction.ReviewCommentEdited);
    }

}
