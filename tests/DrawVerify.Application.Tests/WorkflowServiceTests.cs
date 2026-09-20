using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;

namespace DrawVerify.Application.Tests;

public sealed class WorkflowServiceTests
{
    private static readonly Guid Package = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "TD-02, TD-08 records the approved verification outcome contract")]
    public void Verification_outcomes_retain_audit_fields_and_integrity_classification()
    {
        var stored = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var computed = new DocumentFingerprint { Value = new string('b', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var result = new FingerprintVerificationResult(FingerprintVerificationStatus.Mismatch, computed, "Fingerprint Mismatch - Document May Have Been Altered")
        {
            Stored = stored, VerifiedUtc = new DateTimeOffset(Now), VerifierUserId = "checker-001",
            DocumentId = Package, Revision = 7, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid()
        };

        Assert.Equal(FingerprintVerificationStatus.Mismatch, result.Status);
        Assert.True(result.IsIntegrityFailure);
        Assert.Equal(stored.Value, result.Stored!.Value);
        Assert.Equal(computed.Value, result.Computed!.Value);
        Assert.Equal("checker-001", result.VerifierUserId);
        Assert.Equal(7, result.Revision);
    }

    [Fact(DisplayName = "TD-02 authorizes a checker, selects the requested revision, and audits verification")]
    public async Task Verify_fingerprint_uses_requested_revision_and_records_audit_context()
    {
        var f = Fixture.Create();
        var revisionOne = f.Request;
        var revisionTwo = f.Request;
        f.Workflow.Submissions.Add(new ReviewSubmission
        {
            SubmissionId = Guid.NewGuid(), PackageId = Package, Revision = 1,
            DxfDocumentId = f.Request.DxfDocumentId, StepDocumentVersionId = revisionOne.StepDocumentVersionId,
            DxfDocumentVersionId = revisionOne.DxfDocumentVersionId, MakerUserId = "maker-one", MakerDisplayName = "Maker One",
            SubmittedUtc = Now.AddMinutes(-2), Fingerprint = new DocumentFingerprint { Value = new string('1', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }
        });
        f.Workflow.Submissions.Add(new ReviewSubmission
        {
            SubmissionId = Guid.NewGuid(), PackageId = Package, Revision = 2,
            DxfDocumentId = f.Request.DxfDocumentId, StepDocumentVersionId = revisionTwo.StepDocumentVersionId,
            DxfDocumentVersionId = revisionTwo.DxfDocumentVersionId, MakerUserId = "maker-two", MakerDisplayName = "Maker Two",
            SubmittedUtc = Now, Fingerprint = new DocumentFingerprint { Value = new string('2', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }
        });
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker One" };
        var verifier = new RecordingFingerprintService();
        var service = new WorkflowService(f.Workflow, f.Auth, new WorkflowConfiguration
        {
            PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
            DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved }
        }, new RoleAuthorization(), f.Logger,
            f.CreateDocumentRepository(), stepFiles: new FakeStepFiles(), dxfFiles: new FakeDxfFiles(), fingerprintService: verifier, clock: () => Now);

        var result = await service.VerifyFingerprintAsync(Package, 2);

        Assert.Equal(FingerprintVerificationStatus.Match, result.Status);
        Assert.Equal(2, result.Revision);
        Assert.Equal("maker-two", result.MakerUserId);
        Assert.Equal("checker-001", result.VerifierUserId);
        Assert.Equal(revisionTwo.StepDocumentVersionId, verifier.Context!.StepDocumentVersionId);
        Assert.Equal(revisionTwo.DxfDocumentVersionId, verifier.Context.DxfDocumentVersionId);
        var audit = Assert.Single(f.Workflow.Audits);
        Assert.Equal(DocumentAuditAction.VerificationRunCompleted, audit.Action);
        Assert.Equal("checker-001", audit.UserId);
        Assert.Contains("\"revision\":2", audit.DetailsJson, StringComparison.Ordinal);
        Assert.Contains("storedFingerprint", audit.DetailsJson, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TD-03 fingerprint failure prevents workflow persistence")]
    public async Task Fingerprint_failure_is_visible_and_does_not_submit()
    {
        var f = Fixture.Create();
        var service = new WorkflowService(f.Workflow, f.Auth, new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }, logger: f.Logger, documents: f.CreateDocumentRepository(), stepFiles: new FakeStepFiles(), dxfFiles: new FakeDxfFiles(), fingerprintService: new FailingFingerprintService());

        var result = await service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Empty(f.Workflow.Submissions);
        Assert.Contains("fingerprint", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packageId=", f.Logger.Context);
    }

    private sealed class FakeStepFiles : IStepUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDxfFiles : IDxfUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 4, 5, 6 }));
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact(DisplayName = "TD-06 fingerprint verification result preserves mismatch status")]
    public void Fingerprint_mismatch_is_a_distinct_result()
    {
        var result = new FingerprintVerificationResult(FingerprintVerificationStatus.Mismatch, null, "Fingerprint Mismatch - Document May Have Been Altered");

        Assert.Equal(FingerprintVerificationStatus.Mismatch, result.Status);
        Assert.False(result.IsMatch);
        Assert.Contains("Altered", result.Message);
    }

    [Fact(DisplayName = "TD-08 submission audit details include fingerprint metadata")]
    public void Submission_audit_payload_contains_fingerprint_metadata()
    {
        var fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var auditDetails = System.Text.Json.JsonSerializer.Serialize(new { fingerprint = fingerprint.Value, fingerprintAlgorithm = fingerprint.Algorithm, fingerprintCanonicalizationVersion = fingerprint.CanonicalizationVersion });

        Assert.Contains(fingerprint.Value, auditDetails);
        Assert.Contains("fingerprintAlgorithm", auditDetails);
    }

    [Fact(DisplayName = "TD-APP-01 eligible Maker submission succeeds")]
    public async Task Maker_can_submit_complete_package()
    {
        var f = Fixture.Create(); var result = await f.Service.SubmitForReviewAsync(f.Request);
        Assert.True(result.Succeeded, result.Message); Assert.Equal(WorkflowStatus.SubmittedForReview, result.State!.Status); Assert.Single(f.Workflow.Submissions);
        Assert.Equal(f.Request.StepDocumentVersionId, f.Workflow.Submissions[0].StepDocumentVersionId);
        Assert.Equal(f.Request.DxfDocumentVersionId, f.Workflow.Submissions[0].DxfDocumentVersionId);
        Assert.Equal(f.Request.VerificationSessionId, f.Workflow.Submissions[0].VerificationSessionId);
        Assert.Equal(f.Request.ExtractionEvidenceJson, f.Workflow.Submissions[0].ExtractionEvidenceJson);
        Assert.Equal(f.Request.VerificationEvidenceJson, f.Workflow.Submissions[0].VerificationEvidenceJson);
    }

    [Fact(DisplayName = "TD-APP-02 readiness and authorization failures are explicit and non-mutating")]
    public async Task Rejected_cases_do_not_submit_and_are_audited()
    {
        foreach (var scenario in new (string Name, Action<Fixture> Setup)[]
        {
            ("missing package", f => f.Document = null),
            ("missing STEP version", f => f.Request = f.Request with { StepDocumentVersionId = Guid.Empty }),
            ("missing DXF", f => f.Request = f.Request with { DxfAvailable = false }),
            ("incomplete extraction", f => f.Request = f.Request with { ExtractionCompleted = false }),
            ("incomplete verification", f => f.Request = f.Request with { VerificationCompleted = false }),
            ("processing failure", f => f.Request = f.Request with { ProcessingError = "worker failed" }),
            ("missing metadata", f => f.Request = f.Request with { RequiredMetadataPresent = false }),
            ("persisted extraction error", f => f.Document = f.Document! with { GeometryStatus = CoreGeometryStatus.Error }),
            ("non-Maker", f => f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker })),
            ("duplicate workflow state", f => f.WorkflowState = new WorkflowState { PackageId = Package, Status = WorkflowStatus.UnderReview })
        })
        {
            var f = Fixture.Create(); scenario.Setup(f); var expectedStatus = f.WorkflowState.Status; var result = await f.Service.SubmitForReviewAsync(f.Request);
            Assert.False(result.Succeeded, $"Scenario '{scenario.Name}' unexpectedly succeeded: {result.Message}"); Assert.NotEmpty(result.Errors); Assert.Empty(f.Workflow.Submissions); Assert.Single(f.Workflow.Audits); Assert.Equal(expectedStatus, f.Workflow.InitialStatus);
            Assert.Contains(result.Errors, error => error.Contains("available", StringComparison.OrdinalIgnoreCase) || error.Contains("completed", StringComparison.OrdinalIgnoreCase) || error.Contains("failed", StringComparison.OrdinalIgnoreCase) || error.Contains("missing", StringComparison.OrdinalIgnoreCase) || error.Contains("Maker", StringComparison.OrdinalIgnoreCase) || error.Contains("cannot be submitted", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "TD-APP-02 unauthenticated submission is denied without mutation")]
    public async Task Unauthenticated_actor_cannot_submit()
    {
        var f = Fixture.Create();
        f.Auth.User = null;

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("authenticated Maker", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(f.Workflow.Submissions);
        Assert.Equal(DocumentAuditAction.SubmissionRejected, Assert.Single(f.Workflow.Audits).Action);
    }

    [Theory(DisplayName = "TD-APP-02 duplicate states are rejected without submission")]
    [InlineData(WorkflowStatus.SubmittedForReview)]
    [InlineData(WorkflowStatus.UnderReview)]
    [InlineData(WorkflowStatus.Approved)]
    public async Task Duplicate_states_are_not_mutated(WorkflowStatus status)
    {
        var f = Fixture.Create();
        f.WorkflowState = new WorkflowState { PackageId = Package, Status = status, Revision = 1 };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Contains(status.ToString(), string.Join(" ", result.Errors));
        Assert.Equal(status, f.Workflow.InitialStatus);
        Assert.Empty(f.Workflow.Submissions);
    }

    [Theory(DisplayName = "TD-APP-02 every non-Maker role is denied without mutation")]
    [InlineData(SystemRoles.Checker)]
    [InlineData(SystemRoles.Approver)]
    [InlineData(SystemRoles.Viewer)]
    public async Task Non_maker_roles_cannot_submit(string role)
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser(role, new[] { role }) { UserId = role + "-001", DisplayName = role };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Maker", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(f.Workflow.Submissions);
        Assert.Equal(WorkflowStatus.Draft, f.Workflow.InitialStatus);
        Assert.Equal(DocumentAuditAction.SubmissionRejected, Assert.Single(f.Workflow.Audits).Action);
    }

    [Fact(DisplayName = "TD-APP-03 changed references cannot silently replace submission")]
    public async Task Snapshot_is_captured_once()
    {
        var f = Fixture.Create(); var result = await f.Service.SubmitForReviewAsync(f.Request);
        Assert.True(result.Succeeded, result.Message);
        var saved = result.Submission!;
        var changedRequest = f.Request with { StepDocumentVersionId = Guid.NewGuid() };
        Assert.NotEqual(changedRequest.StepDocumentVersionId, saved.StepDocumentVersionId); Assert.Equal("{\"extracted\":true}", saved.ExtractionEvidenceJson);
    }

    [Fact(DisplayName = "TD-APP-02 stale STEP version is rejected")]
    public async Task Stale_step_version_is_rejected_without_submission()
    {
        var f = Fixture.Create();
        f.Request = f.Request with { StepDocumentVersionId = Guid.NewGuid() };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(f.Workflow.Submissions);
    }

    [Fact(DisplayName = "TD-APP-02 verification processing failure is explicit and non-mutating")]
    public async Task Verification_processing_failure_is_rejected_without_submission()
    {
        var f = Fixture.Create();
        f.Request = f.Request with { VerificationError = "verification worker failed" };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("Verification failed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(WorkflowStatus.Draft, f.Workflow.InitialStatus);
        Assert.Empty(f.Workflow.Submissions);
        Assert.Equal(DocumentAuditAction.SubmissionRejected, Assert.Single(f.Workflow.Audits).Action);
    }

    [Fact(DisplayName = "TSD-AC01-TRIGGER submission notifies the assigned Checker once")]
    public async Task TSD_AC01_TRIGGER_notifies_assigned_checker_once_with_committed_submission()
    {
        var f = Fixture.Create();
        var notifications = new RecordingNotificationService();
        f.Notifications = notifications;
        f.Configuration = new WorkflowConfiguration
        {
            PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
            DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved },
            Notifications = new[] { new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, Enabled = true, Channels = new[] { WorkflowNotificationChannel.InApp } } }
        };
        f.Request = f.Request with { AssignedCheckerUserId = "C-1" };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        var call = Assert.Single(notifications.Submissions);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(result.Submission!.SubmissionId, call.SubmissionId);
        Assert.Equal(1, call.Revision);
        Assert.Equal(new[] { "C-1" }, call.AssignedCheckerUserIds);
    }

    [Fact(DisplayName = "TSD-AC02-CHANNELS configured supported channel is delivered")]
    public async Task TSD_AC02_CHANNELS_uses_configured_supported_channel()
    {
        var f = Fixture.Create();
        var notifications = new RecordingNotificationService();
        f.Notifications = notifications;
        f.Configuration = f.Configuration with { Notifications = new[] { new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, Channels = new[] { WorkflowNotificationChannel.InApp } } } };
        f.Request = f.Request with { AssignedCheckerUserId = "C-1" };

        await f.Service.SubmitForReviewAsync(f.Request);

        Assert.Contains(WorkflowNotificationChannel.InApp, Assert.Single(notifications.Channels));
    }

    [Fact(DisplayName = "TSD-AC06-PREFS disabled submission notification is not enabled")]
    public async Task TSD_AC06_PREFS_disabled_submission_rule_does_not_deliver()
    {
        var f = Fixture.Create();
        var notifications = new RecordingNotificationService();
        f.Notifications = notifications;
        f.Configuration = f.Configuration with { Notifications = new[] { new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, Enabled = false } } };
        f.Request = f.Request with { AssignedCheckerUserId = "C-1" };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(notifications.Submissions);
    }

    [Fact(DisplayName = "TSD-AC07-RECIPIENTS preserves every assigned Checker")]
    public async Task TSD_AC07_RECIPIENTS_notifies_each_assigned_checker()
    {
        var f = Fixture.Create();
        var notifications = new RecordingNotificationService();
        f.Notifications = notifications;
        f.Configuration = f.Configuration with { Notifications = new[] { new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, Channels = new[] { WorkflowNotificationChannel.InApp } } } };
        f.Request = f.Request with { AssignedCheckerUserIds = new[] { "C-1", "C-2" } };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(new[] { "C-1", "C-2" }, notifications.Submissions.Single().AssignedCheckerUserIds);
    }

    [Fact(DisplayName = "TSD-AC10-IDEMPOTENCY distinct submission identities remain distinct")]
    public void TSD_AC10_IDEMPOTENCY_submission_identity_is_not_replaced_by_revision()
    {
        var first = new ReviewSubmission { PackageId = Package, Revision = 3, SubmissionId = Guid.NewGuid(), AssignedCheckerUserIds = new[] { "C-1" } };
        var second = first with { SubmissionId = Guid.NewGuid(), Revision = 4 };

        Assert.NotEqual(first.SubmissionId, second.SubmissionId);
        Assert.Equal("C-1", Assert.Single(second.AssignedCheckerUserIds));
    }

    [Fact(DisplayName = "TD-APP-01 InPreparation is an eligible initial state")]
    public async Task In_preparation_package_can_be_submitted()
    {
        var f = Fixture.Create();
        f.WorkflowState = new WorkflowState { PackageId = Package, Status = WorkflowStatus.InPreparation, Revision = 1 };

        var result = await f.Service.SubmitForReviewAsync(f.Request);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(WorkflowStatus.SubmittedForReview, result.State!.Status);
        Assert.Equal(WorkflowStatus.InPreparation, f.Workflow.InitialStatus);
        Assert.Single(f.Workflow.Submissions);
    }

    [Fact(DisplayName = "TD-APP-03 changed package is marked superseded")]
    public async Task Changed_document_reference_supersedes_submission()
    {
        var f = Fixture.Create();
        var result = await f.Service.SubmitForReviewAsync(f.Request);
        Assert.True(result.Succeeded, result.Message);
        var submission = result.Submission!;
        f.WorkflowState = f.WorkflowState with { Status = WorkflowStatus.SubmittedForReview };
        f.Document = f.Document! with { Version = new DocumentVersion { VersionId = Guid.NewGuid() } };

        var state = await f.Service.GetPackageAsync(Package);

        Assert.Equal(WorkflowStatus.Superseded, state!.Status);
        Assert.Equal(submission.SubmissionId, f.Workflow.SupersededSubmissionId);
        Assert.Equal(WorkflowStatus.SubmittedForReview, submission.Status);
    }

    [Fact(DisplayName = "TD-APP-04 details and persistence exceptions are observable")]
    public async Task Details_are_trimmed_and_original_exception_is_preserved()
    {
        var f = Fixture.Create(); f.Request = f.Request with { Note = "  Please review hole tolerances  " }; var result = await f.Service.SubmitForReviewAsync(f.Request);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Please review hole tolerances", result.Submission!.Note); Assert.Equal("maker-001", result.Submission.MakerUserId); Assert.Equal("Maker One", result.Submission.MakerDisplayName); Assert.Equal(Now, result.Submission.SubmittedUtc); Assert.Equal(1, result.Submission.Revision);
        f = Fixture.Create(); f.Workflow.ThrowOnSubmit = true; var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitForReviewAsync(f.Request)); Assert.Equal("persist failed", error.Message); Assert.Contains("packageId=", f.Logger.Context);
    }

    [Fact(DisplayName = "TD-APP-04 successful audit contains maker, revision, result, and timestamp")]
    public async Task Successful_submission_audit_contains_required_context()
    {
        var f = Fixture.Create();

        var result = await f.Service.SubmitForReviewAsync(f.Request);
        var audit = Assert.Single(f.Workflow.Audits);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(DocumentAuditAction.MakerSignatureApplied, audit.Action);
        Assert.Equal("maker-001", audit.UserId);
        Assert.Equal(Now, audit.TimestampUtc);
        Assert.Contains("packageId", audit.DetailsJson);
        Assert.Contains("revision", audit.DetailsJson);
        Assert.Contains("Submitted", audit.DetailsJson);
    }

    [Fact(DisplayName = "TD-APP-05 cancellation has no submit mutation and queue projection is exposed")]
    public async Task Cancellation_records_only_audit_and_queue_is_readable()
    {
        var f = Fixture.Create(); await f.Service.RecordCancellationAsync(Package, 1, "keep this note");
        Assert.Empty(f.Workflow.Submissions); var audit = Assert.Single(f.Workflow.Audits);
        Assert.Equal(DocumentAuditAction.SubmissionCancelled, audit.Action); Assert.Equal("maker-001", audit.UserId);
        Assert.Contains("keep this note", audit.DetailsJson); Assert.Contains(Package.ToString(), audit.DetailsJson);
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker One" };
        Assert.Empty(await f.Service.GetCheckerQueueAsync());
    }

    [Fact(DisplayName = "TD-APP-04 audit details exclude the optional note payload")]
    public async Task Successful_audit_contains_context_without_sensitive_note()
    {
        var f = Fixture.Create();
        f.Request = f.Request with { Note = "internal-only note" };

        await f.Service.SubmitForReviewAsync(f.Request);

        var audit = Assert.Single(f.Workflow.Audits);
        Assert.Contains("maker-001", audit.DetailsJson);
        Assert.Contains("Maker One", audit.DetailsJson);
        Assert.DoesNotContain("internal-only note", audit.DetailsJson);
    }

    [Fact(DisplayName = "TD-APP-049 Checker queue is authorized and projects submitted states")]
    public async Task Checker_queue_requires_review_permission()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" };
        f.Workflow.Queue.Add(new WorkflowQueueItem(Package, 7, "Maker One", Now, WorkflowStatus.SubmittedForReview, "verified"));
        f.Workflow.Queue.Add(new WorkflowQueueItem(Guid.NewGuid(), 8, "Maker Two", Now, WorkflowStatus.UnderReview, "missing"));
        Assert.Equal(2, (await f.Service.GetCheckerQueueAsync()).Count);
        f.Auth.User = new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetCheckerQueueAsync());
    }

    [Fact(DisplayName = "TD-APP-050 opening a package records review and returns immutable evidence")]
    public async Task Opening_review_package_records_start_and_exact_evidence()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" };
        f.Workflow.ReviewPackage = ReviewPackageFixture();
        var result = await f.Service.OpenReviewPackageAsync(Package, 7);
        Assert.Equal(f.Workflow.ReviewPackage, result);
        Assert.Equal(1, f.Workflow.ReviewStartedCalls);
        Assert.Equal("{\"dimensions\":[10]}", result!.ExtractionEvidenceJson);
        Assert.Equal("{\"missing\":[\"D1\"]}", result.VerificationEvidenceJson);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000011"), result.StepDocumentVersionId);
    }

    [Theory(DisplayName = "TD-APP-051 review decisions validate comments and persist expected revision")]
    [InlineData(ReviewDecisionType.Approve, "", WorkflowStatus.Approved)]
    [InlineData(ReviewDecisionType.Reject, "fix dimensions", WorkflowStatus.Rejected)]
    [InlineData(ReviewDecisionType.RequestChanges, "clarify GD&T", WorkflowStatus.ChangesRequested)]
    public async Task Review_decision_branches_are_explicit(ReviewDecisionType type, string comments, WorkflowStatus expected)
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" };
        f.Workflow.ReviewPackage = ReviewPackageFixture();
        var saved = await f.Service.DecideAsync(Package, 7, type, comments);
        Assert.Equal(type, saved.DecisionType); Assert.Equal(expected, f.Workflow.SavedStatus); Assert.Equal(7, f.Workflow.LastDecision!.Revision);
        Assert.Equal("checker-1", saved.CheckerUserId); Assert.Equal(1, f.Workflow.DecisionCalls);
        f.Workflow.ReviewPackage = f.Workflow.ReviewPackage with { PriorDecision = saved };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.DecideAsync(Package, 7, ReviewDecisionType.Approve, null));
    }

    [Theory(DisplayName = "TD-APP-051 blank reject or request-change comments do not persist")]
    [InlineData(ReviewDecisionType.Reject)]
    [InlineData(ReviewDecisionType.RequestChanges)]
    public async Task Blank_required_decision_comments_are_rejected_without_mutation(ReviewDecisionType type)
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" };
        f.Workflow.ReviewPackage = ReviewPackageFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.DecideAsync(Package, 7, type, " \t"));

        Assert.Equal(0, f.Workflow.DecisionCalls);
        Assert.Null(f.Workflow.SavedStatus);
        Assert.Empty(f.Workflow.Audits);
    }

    [Fact(DisplayName = "TD-APP-049 and TD-APP-051 unauthorized review operations are non-mutating")]
    public async Task Unauthorized_checker_operations_are_denied()
    {
        var f = Fixture.Create();
        f.Workflow.ReviewPackage = ReviewPackageFixture();
        f.Auth.User = new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }) { UserId = "viewer-1", DisplayName = "Viewer" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetCheckerQueueAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.DecideAsync(Package, 7, ReviewDecisionType.Approve, null));

        Assert.Equal(0, f.Workflow.DecisionCalls);
        Assert.Empty(f.Workflow.Audits);
    }

    [Fact(DisplayName = "TD-APP-052 review comments do not change decision or status")]
    public async Task Review_comment_is_saved_with_author_and_trimmed_text()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" };
        var comment = await f.Service.AddReviewCommentAsync(Package, 7, "  inspect control  ");
        Assert.Equal("inspect control", comment.Text); Assert.Equal("checker-1", comment.AuthorUserId);
        Assert.Equal(0, f.Workflow.DecisionCalls); Assert.Null(f.Workflow.SavedStatus); Assert.Single(f.Workflow.Comments);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.AddReviewCommentAsync(Package, 7, " "));
    }

    [Fact(DisplayName = "TD-APP-01 aggregates exact prior revision metadata and preserves context")]
    public async Task Prior_versions_are_aggregated_without_cross_revision_data()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" };
        var revisions = new[] { WorkflowStatus.Rejected, WorkflowStatus.Approved, WorkflowStatus.Draft }
            .Select((status, index) => new ReviewSubmission
            {
                SubmissionId = Guid.Parse($"00000000-0000-0000-0000-0000000000{21 + index}"), PackageId = Package, Revision = index + 1,
                StepDocumentVersionId = Guid.Parse($"00000000-0000-0000-0000-0000000001{21 + index}"), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(),
                MakerUserId = $"maker-{index + 1}", MakerDisplayName = $"Maker {index + 1}", Status = status,
                SubmittedUtc = Now.AddDays(index), CreatedUtc = Now.AddDays(index), Note = $"revision {index + 1}", VerificationEvidenceJson = $"{{\"revision\":{index + 1}}}"
            }).ToArray();
        f.Workflow.Submissions.AddRange(revisions);
        foreach (var submission in revisions)
            f.Workflow.ReviewPackages[submission.Revision] = ReviewPackage.FromSubmission(submission);

        var result = await f.Service.GetPriorVersionsAsync(Package);

        Assert.Equal(new[] { 3, 2, 1 }, result.Select(x => x.Revision));
        Assert.All(result, x => Assert.Equal(revisions.Single(s => s.Revision == x.Revision).StepDocumentVersionId, x.StepDocumentVersionId));
        Assert.Equal("maker-1", (await f.Service.GetPriorVersionAsync(Package, 1))!.MakerUserId);
        Assert.Equal(WorkflowStatus.Rejected, result.Single(x => x.Revision == 1).Status);
        Assert.Null(await f.Service.GetPriorVersionAsync(Package, 99));
    }

    [Fact(DisplayName = "TD-APP-03 authorizes prior access and audits successful detail access")]
    public async Task Prior_version_access_has_role_matrix_and_success_audit()
    {
        var f = Fixture.Create();
        var submission = new ReviewSubmission { PackageId = Package, Revision = 1, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), MakerUserId = "maker", MakerDisplayName = "Maker" };
        f.Workflow.Submissions.Add(submission); f.Workflow.ReviewPackages[1] = ReviewPackage.FromSubmission(submission);
        foreach (var role in new[] { SystemRoles.Maker, SystemRoles.Viewer })
        {
            f.Auth.User = new AuthenticatedUser(role, new[] { role });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetPriorVersionsAsync(Package));
            Assert.DoesNotContain(f.Workflow.Audits, x => x.Action == DocumentAuditAction.PriorVersionViewed);
        }
        f.Auth.User = new AuthenticatedUser(SystemRoles.Checker, new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" };
        await f.Service.GetPriorVersionAsync(Package, 1);
        var audit = Assert.Single(f.Workflow.Audits, x => x.Action == DocumentAuditAction.PriorVersionViewed);
        Assert.Equal(Package, audit.DocumentId); Assert.Equal("checker-001", audit.UserId); Assert.Contains("PriorVersionViewed", audit.DetailsJson);
    }

    [Fact(DisplayName = "TD-APP-04 filters and compares prior evidence deterministically")]
    public async Task Prior_versions_filter_and_comparison_are_deterministic()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser(SystemRoles.Approver, new[] { SystemRoles.Approver });
        var prior = new ReviewSubmission { PackageId = Package, Revision = 1, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), MakerUserId = "maker-a", MakerDisplayName = "Maker A", Status = WorkflowStatus.Rejected, SubmittedUtc = Now };
        var current = prior with { SubmissionId = Guid.NewGuid(), Revision = 2, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), Status = WorkflowStatus.SubmittedForReview, MakerUserId = "maker-b", SubmittedUtc = Now.AddDays(1), VerificationEvidenceJson = "{\"changed\":true}" };
        f.Workflow.Submissions.AddRange(new[] { prior, current }); f.Workflow.ReviewPackages[1] = ReviewPackage.FromSubmission(prior); f.Workflow.ReviewPackages[2] = ReviewPackage.FromSubmission(current);
        var filtered = await f.Service.GetPriorVersionsAsync(Package, new PriorVersionFilter { Status = WorkflowStatus.Rejected, FromUtc = Now, ToUtc = Now, MakerUserId = "MAKER-A" });
        Assert.Single(filtered); Assert.Equal(1, filtered[0].Revision);
        var comparison = await f.Service.CompareVersionsAsync(Package, 2, 1);
        Assert.Equal(2, comparison.CurrentRevision); Assert.NotEmpty(comparison.FileChanges); Assert.NotEmpty(comparison.MetadataChanges); Assert.False(comparison.EvidenceAvailable);
    }

    [Theory(DisplayName = "TD-02 eligible rejection states and authorized roles are enforced")]
    [InlineData(WorkflowStatus.SubmittedForReview, SystemRoles.Checker, ReviewDecisionType.Reject)]
    [InlineData(WorkflowStatus.UnderReview, SystemRoles.Checker, ReviewDecisionType.Return)]
    [InlineData(WorkflowStatus.SubmittedForReview, SystemRoles.Approver, ReviewDecisionType.Reject)]
    public async Task Authorized_rejection_is_saved_for_eligible_states(WorkflowStatus status, string role, ReviewDecisionType type)
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser(role, new[] { role }) { UserId = role + "-001", DisplayName = role };
        f.Workflow.ReviewPackage = ReviewPackageFixture() with { Status = status };
        var decision = await f.Service.DecideAsync(Package, 7, type, "Dimension 4 is outside tolerance");
        Assert.Equal(type, decision.DecisionType);
        Assert.Equal(type == ReviewDecisionType.Return ? WorkflowStatus.ReturnedForRework : WorkflowStatus.Rejected, f.Workflow.SavedStatus);
        Assert.Equal(1, f.Workflow.DecisionCalls);
    }

    [Theory(DisplayName = "TD-02 blank rejection reasons and terminal states are non-mutating")]
    [InlineData(WorkflowStatus.SubmittedForReview, " ")]
    [InlineData(WorkflowStatus.UnderReview, "\t")]
    [InlineData(WorkflowStatus.Rejected, "late decision")]
    [InlineData(WorkflowStatus.ReturnedForRework, "late decision")]
    [InlineData(WorkflowStatus.FinallyApproved, "late decision")]
    [InlineData(WorkflowStatus.Superseded, "late decision")]
    public async Task Invalid_rejection_does_not_mutate_status_decision_or_audit(WorkflowStatus status, string reason)
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" };
        f.Workflow.ReviewPackage = ReviewPackageFixture() with { Status = status };
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.DecideAsync(Package, 7, ReviewDecisionType.Reject, reason));
        Assert.Equal(0, f.Workflow.DecisionCalls);
        Assert.Null(f.Workflow.SavedStatus);
        Assert.Empty(f.Workflow.Audits);
    }

    [Fact(DisplayName = "TD-03 draft comments support references and preserve review status")]
    public async Task Draft_comment_save_preserves_status_and_audit_context()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" };
        f.Workflow.ReviewPackage = ReviewPackageFixture() with { Status = WorkflowStatus.UnderReview };
        var comment = await f.Service.SaveReviewCommentDraftAsync(Package, 7, "Fix dimension 4", "dimension", "DIM-4", "sheet-1");
        Assert.True(comment.IsDraft);
        Assert.Equal("dimension", comment.ReferenceType);
        Assert.Equal(WorkflowStatus.UnderReview, f.Workflow.ReviewPackage.Status);
        Assert.Contains(f.Workflow.Audits, x => x.Action == DocumentAuditAction.ReviewCommentDraftSaved);
        var edited = await f.Service.UpdateReviewCommentAsync(comment with { Text = "Fix dimension 4 tolerance" });
        Assert.Equal("Fix dimension 4 tolerance", edited.Text);
        Assert.Contains(f.Workflow.Audits, x => x.Action == DocumentAuditAction.ReviewCommentEdited);
        await f.Service.DiscardReviewCommentAsync(Package, 7, comment.CommentId);
        Assert.Contains(f.Workflow.Audits, x => x.Action == DocumentAuditAction.ReviewCommentDiscarded);
        Assert.Null(f.Workflow.SavedStatus);
    }

    [Fact(DisplayName = "TD-06 prior rejection comments and correction responses remain linked")]
    public void Resubmission_context_keeps_prior_issue_pairing()
    {
        var prior = new ReviewComment { PackageId = Package, Revision = 1, AuthorUserId = "checker-001", Text = "Fix DIM-4", ReferenceType = "dimension", ReferenceId = "DIM-4", IsDraft = false };
        var response = new ReviewComment { PackageId = Package, Revision = 2, AuthorUserId = "maker-001", Text = "Corrected DIM-4", PriorCommentId = prior.CommentId, CorrectionResponse = "Applied tolerance correction", IsDraft = false };
        prior.Validate(); response.Validate();
        var revision2 = ReviewPackageFixture() with { Revision = 2, Comments = new[] { prior, response }, PriorDecision = new ReviewDecision { PackageId = Package, Revision = 1, CheckerUserId = "checker-001", DecisionType = ReviewDecisionType.Reject, Comments = "Fix DIM-4", OverallReason = "Dimension 4 is outside tolerance" } };
        Assert.Equal(prior.CommentId, response.PriorCommentId);
        Assert.Equal("Applied tolerance correction", revision2.Comments.Single(x => x.PriorCommentId == prior.CommentId).CorrectionResponse);
        Assert.Single(revision2.PriorComments);
    }

    [Fact(DisplayName = "TD-08 failed rejection does not emit a false notification or audit")]
    public async Task Failed_rejection_has_no_success_side_effects()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }) { UserId = "viewer-001", DisplayName = "Viewer" };
        f.Workflow.ReviewPackage = ReviewPackageFixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.DecideAsync(Package, 7, ReviewDecisionType.Reject, "not authorized"));
        Assert.Equal(0, f.Workflow.DecisionCalls);
        Assert.Empty(f.Workflow.Audits);
    }

    [Fact(DisplayName = "TD-09 existing approval behavior remains unchanged")]
    public async Task Approval_still_uses_existing_approved_status()
    {
        var f = Fixture.Create();
        f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" };
        f.Workflow.ReviewPackage = ReviewPackageFixture();
        var saved = await f.Service.DecideAsync(Package, 7, ReviewDecisionType.Approve, null);
        Assert.Equal(ReviewDecisionType.Approve, saved.DecisionType);
        Assert.Equal(WorkflowStatus.Approved, f.Workflow.SavedStatus);
    }

    private static ReviewPackage ReviewPackageFixture() => ReviewPackage.FromSubmission(new ReviewSubmission
    {
        PackageId = Package, Revision = 7, StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"),
        DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"), VerificationSessionId = Guid.NewGuid(),
        ExtractionEvidenceJson = "{\"dimensions\":[10]}", VerificationEvidenceJson = "{\"missing\":[\"D1\"]}",
        MakerUserId = "maker-1", MakerDisplayName = "Maker One", SubmittedUtc = Now
    });

    private sealed class Fixture
    {
        private static readonly Guid DxfDocument = Guid.Parse("00000000-0000-0000-0000-000000000002");
        private static readonly Guid DxfVersion = Guid.Parse("00000000-0000-0000-0000-000000000012");
        public FakeWorkflow Workflow { get; } = new(); public FakeAuth Auth { get; } = new(); public FakeLogger Logger { get; } = new();
        public Document? Document { get; set; } = new() { DocumentId = Package, DocumentType = DrawVerify.Core.Enums.DocumentType.StepModel, OriginalFileName = "package.step", FileSizeBytes = 1024, GeometryStatus = CoreGeometryStatus.Processed, Version = new DocumentVersion { VersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DocumentId = Package } };
        public Document DxfDocumentRecord { get; } = new() { DocumentId = DxfDocument, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, OriginalFileName = "drawing.dxf", FileSizeBytes = 1024, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, VerificationStatus = VerificationStatus.DimensionsExtracted, Version = new DocumentVersion { VersionId = DxfVersion, DocumentId = DxfDocument }, Versions = new[] { new DocumentVersion { VersionId = DxfVersion, DocumentId = DxfDocument } } };
        public WorkflowState WorkflowState { get => _state; set { _state = value; Workflow.InitialStatus = value.Status; } } private WorkflowState _state = new() { PackageId = Package, Status = WorkflowStatus.Draft, Revision = 1 };
        public WorkflowConfiguration Configuration { get; set; } = new() { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } };
        public RecordingNotificationService? Notifications { get; set; }
        public SubmissionRequest Request { get; set; } = new(Package) { DxfDocumentId = DxfDocument, StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DxfDocumentVersionId = DxfVersion, VerificationSessionId = Guid.Parse("00000000-0000-0000-0000-000000000013"), ExtractionEvidenceJson = "{\"extracted\":true}", VerificationEvidenceJson = "{\"verified\":true}", MakerSignature = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Package, 1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) };
        public WorkflowService Service => new(Workflow, Auth, Configuration, new RoleAuthorization(), Logger, new FakeDocuments(this), null, () => Now, Notifications, configurationRepository: new MemoryWorkflowConfigurationRepository(Configuration));
        public IDocumentRepository CreateDocumentRepository() => new FakeDocuments(this);
        public static Fixture Create() { var f = new Fixture(); f.Workflow.StateProvider = () => f._state; return f; }
        private sealed class FakeDocuments(Fixture owner) : IDocumentRepository
        {
            public Task<Document?> GetAsync(Guid id, CancellationToken ct = default)
            {
                if (id == DxfDocument)
                    return Task.FromResult<Document?>(owner.Request.DxfAvailable ? owner.DxfDocumentRecord with
                    {
                        UploadStatus = owner.Request.DxfAvailable ? DrawVerify.Core.Enums.UploadStatus.Uploaded : DrawVerify.Core.Enums.UploadStatus.Failed,
                        VerificationStatus = owner.Request.VerificationCompleted ? VerificationStatus.DimensionsExtracted : VerificationStatus.Unknown,
                        OriginalFileName = owner.Request.RequiredMetadataPresent ? "drawing.dxf" : string.Empty,
                        FileSizeBytes = owner.Request.RequiredMetadataPresent ? 1024 : 0
                    } : null);

                if (id != Package || owner.Document is null)
                    return Task.FromResult<Document?>(null);

                return Task.FromResult<Document?>(owner.Document with
                {
                    GeometryStatus = owner.Document.GeometryStatus == CoreGeometryStatus.Error || owner.Request.ProcessingError is not null ? CoreGeometryStatus.Error : owner.Request.ExtractionCompleted ? CoreGeometryStatus.Processed : CoreGeometryStatus.NotExtracted,
                    OriginalFileName = owner.Request.RequiredMetadataPresent ? "package.step" : string.Empty,
                    FileSizeBytes = owner.Request.RequiredMetadataPresent ? 1024 : 0
                });
            }

            public Task<DocumentVersion?> GetVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
            {
                var version = documentId == Package && owner.Document?.Version?.VersionId == versionId
                    ? owner.Document.Version
                    : null;
                return Task.FromResult(version);
            }
        }
    }
    private sealed class FakeAuth : ISessionAuthenticationService { public AuthenticatedUser? User { get; set; } = CreateMaker(); private static AuthenticatedUser CreateMaker() => new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" }; public AuthenticatedUser? CurrentUser => User; public bool IsAuthenticated => User is not null; public void SignIn(AuthenticatedUser user) => User = user; public void SignOut() => User = null; }
    private sealed class FakeLogger : ICentralizedLogger { public string Context { get; private set; } = ""; public void Log(Exception exception, string operation, string context) => Context = context; }
    private sealed class MemoryWorkflowConfigurationRepository(WorkflowConfiguration configuration) : IWorkflowConfigurationRepository
    {
        private readonly Dictionary<string, int?> _capturedVersions = new(StringComparer.Ordinal);
        public Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult<WorkflowConfiguration?>(configuration);
        public Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowConfiguration?>(configuration.Version == version ? configuration : null);
        public Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration value, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<int?> GetWorkflowVersionAsync(string workflowId, CancellationToken cancellationToken = default) => Task.FromResult(_capturedVersions.TryGetValue(workflowId, out var version) ? version : null);
        public Task CaptureWorkflowVersionAsync(string workflowId, int? version, CancellationToken cancellationToken = default) { _capturedVersions[workflowId] = version; return Task.CompletedTask; }
    }
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<ReviewSubmission> Submissions { get; } = new();
        public List<IReadOnlyList<WorkflowNotificationChannel>> Channels { get; } = new();
        public Task NotifySubmissionAsync(ReviewSubmission submission, IReadOnlyList<WorkflowNotificationChannel> channels, CancellationToken cancellationToken = default) { Submissions.Add(submission); Channels.Add(channels); return Task.CompletedTask; }
    }
    private sealed class FailingFingerprintService : IDocumentFingerprintService
    {
        public Task<DocumentFingerprint> ComputeAsync(DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => Task.FromException<DocumentFingerprint>(new IOException("hash input failed"));
        public Task<FingerprintVerificationResult> VerifyAsync(DocumentFingerprint expected, DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, CancellationToken cancellationToken = default) => Task.FromException<FingerprintVerificationResult>(new IOException("verification failed"));
    }
    private sealed class RecordingFingerprintService : IDocumentFingerprintService
    {
        public DocumentFingerprintContext? Context { get; private set; }
        public Task<DocumentFingerprint> ComputeAsync(DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new DocumentFingerprint { Value = new string('2', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 });
        }
        public Task<FingerprintVerificationResult> VerifyAsync(DocumentFingerprint expected, DocumentFingerprintContext context, IReadOnlyCollection<DocumentFingerprintArtifact> artifacts, CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new FingerprintVerificationResult(FingerprintVerificationStatus.Match, expected, "Fingerprint Verified - Document Unchanged") { Stored = expected });
        }
    }
    private sealed class FakeWorkflow : IWorkflowRepository { public Func<WorkflowState>? StateProvider { get; set; } public WorkflowStatus InitialStatus { get; set; } = WorkflowStatus.Draft; public bool ThrowOnSubmit { get; set; } public Guid? SupersededSubmissionId { get; private set; } public List<ReviewSubmission> Submissions { get; } = new(); public List<AuditEntry> Audits { get; } = new(); public List<WorkflowQueueItem> Queue { get; } = new(); public ReviewPackage? ReviewPackage { get; set; } public Dictionary<int, ReviewPackage> ReviewPackages { get; } = new(); public int ReviewStartedCalls { get; private set; } public int DecisionCalls { get; private set; } public ReviewDecision? LastDecision { get; private set; } public WorkflowStatus? SavedStatus { get; private set; } public List<ReviewComment> Comments { get; } = new(); public Task<WorkflowState?> GetStateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WorkflowState?>(StateProvider?.Invoke()); public Task<ReviewSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReviewSubmission?>(Submissions.FirstOrDefault(x => x.SubmissionId == id)); public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Submissions); public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Queue); public Task<ReviewPackage?> GetReviewPackageAsync(Guid id, int revision, CancellationToken ct = default) => Task.FromResult(ReviewPackages.TryGetValue(revision, out var package) ? package : ReviewPackage); public Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(Guid id, int revision, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewComment>>(Comments); public Task SaveReviewCommentAsync(ReviewComment comment, AuditEntry a, CancellationToken ct = default) { Comments.Add(comment); Audits.Add(a); return Task.CompletedTask; } public Task UpdateReviewCommentAsync(ReviewComment comment, AuditEntry a, CancellationToken ct = default) { var index = Comments.FindIndex(x => x.CommentId == comment.CommentId); if (index >= 0) Comments[index] = comment; Audits.Add(a); return Task.CompletedTask; } public Task DiscardReviewCommentAsync(Guid commentId, AuditEntry a, CancellationToken ct = default) { Comments.RemoveAll(x => x.CommentId == commentId); Audits.Add(a); return Task.CompletedTask; } public Task RecordReviewStartedAsync(ReviewPackage package, AuditEntry a, CancellationToken ct = default) { ReviewStartedCalls++; Audits.Add(a); return Task.CompletedTask; } public Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage package, ReviewDecision decision, WorkflowStatus status, AuditEntry a, CancellationToken ct = default) { DecisionCalls++; LastDecision = decision; SavedStatus = status; Audits.Add(a); return Task.FromResult(decision); } public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState s, ReviewSubmission sub, AuditEntry a, CancellationToken ct = default) { if (ThrowOnSubmit) throw new InvalidOperationException("persist failed"); Submissions.Add(sub); Audits.Add(a); return Task.FromResult(sub); } public Task RecordAuditAsync(AuditEntry a, CancellationToken ct = default) { Audits.Add(a); return Task.CompletedTask; } public Task MarkSupersededAsync(Guid id, AuditEntry a, CancellationToken ct = default) { SupersededSubmissionId = id; return Task.CompletedTask; } }
}
