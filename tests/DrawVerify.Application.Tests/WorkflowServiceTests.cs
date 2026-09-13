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

    [Fact(DisplayName = "TD-APP-01 eligible Maker submission succeeds")]
    public async Task Maker_can_submit_complete_package()
    {
        var f = Fixture.Create(); var result = await f.Service.SubmitForReviewAsync(f.Request);
        Assert.True(result.Succeeded); Assert.Equal(WorkflowStatus.SubmittedForReview, result.State!.Status); Assert.Single(f.Workflow.Submissions);
        Assert.Equal(f.Request.StepDocumentVersionId, f.Workflow.Submissions[0].StepDocumentVersionId);
        Assert.Equal(f.Request.DxfDocumentVersionId, f.Workflow.Submissions[0].DxfDocumentVersionId);
        Assert.Equal(f.Request.VerificationSessionId, f.Workflow.Submissions[0].VerificationSessionId);
        Assert.Equal(f.Request.ExtractionEvidenceJson, f.Workflow.Submissions[0].ExtractionEvidenceJson);
        Assert.Equal(f.Request.VerificationEvidenceJson, f.Workflow.Submissions[0].VerificationEvidenceJson);
    }

    [Fact(DisplayName = "TD-APP-02 readiness and authorization failures are explicit and non-mutating")]
    public async Task Rejected_cases_do_not_submit_and_are_audited()
    {
        foreach (var setup in new Action<Fixture>[]
        {
            f => f.Document = null,
            f => f.Request = f.Request with { StepDocumentVersionId = Guid.Empty },
            f => f.Request = f.Request with { DxfAvailable = false },
            f => f.Request = f.Request with { ExtractionCompleted = false },
            f => f.Request = f.Request with { VerificationCompleted = false },
            f => f.Request = f.Request with { ProcessingError = "worker failed" },
            f => f.Request = f.Request with { RequiredMetadataPresent = false },
            f => f.Document = f.Document! with { GeometryStatus = CoreGeometryStatus.Error },
            f => f.Auth.User = new AuthenticatedUser("checker", new[] { SystemRoles.Checker }),
            f => f.WorkflowState = new WorkflowState { PackageId = Package, Status = WorkflowStatus.UnderReview }
        })
        {
            var f = Fixture.Create(); setup(f); var expectedStatus = f.WorkflowState.Status; var result = await f.Service.SubmitForReviewAsync(f.Request);
            Assert.False(result.Succeeded); Assert.NotEmpty(result.Errors); Assert.Empty(f.Workflow.Submissions); Assert.Single(f.Workflow.Audits); Assert.Equal(expectedStatus, f.Workflow.InitialStatus);
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
        var f = Fixture.Create(); var saved = (await f.Service.SubmitForReviewAsync(f.Request)).Submission!;
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

    [Fact(DisplayName = "TD-APP-03 changed package is marked superseded")]
    public async Task Changed_document_reference_supersedes_submission()
    {
        var f = Fixture.Create();
        var submission = (await f.Service.SubmitForReviewAsync(f.Request)).Submission!;
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
        Assert.Equal("Please review hole tolerances", result.Submission!.Note); Assert.Equal("maker-001", result.Submission.MakerUserId); Assert.Equal("Maker One", result.Submission.MakerDisplayName); Assert.Equal(Now, result.Submission.SubmittedUtc); Assert.Equal(1, result.Submission.Revision);
        f = Fixture.Create(); f.Workflow.ThrowOnSubmit = true; var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitForReviewAsync(f.Request)); Assert.Equal("persist failed", error.Message); Assert.Contains("packageId=", f.Logger.Context);
    }

    [Fact(DisplayName = "TD-APP-04 successful audit contains maker, revision, result, and timestamp")]
    public async Task Successful_submission_audit_contains_required_context()
    {
        var f = Fixture.Create();

        var result = await f.Service.SubmitForReviewAsync(f.Request);
        var audit = Assert.Single(f.Workflow.Audits);

        Assert.True(result.Succeeded);
        Assert.Equal(DocumentAuditAction.SubmissionSucceeded, audit.Action);
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

    private sealed class Fixture
    {
        public FakeWorkflow Workflow { get; } = new(); public FakeAuth Auth { get; } = new(); public FakeLogger Logger { get; } = new();
        public Document? Document { get; set; } = new() { DocumentId = Package, GeometryStatus = CoreGeometryStatus.Processed, Version = new DocumentVersion { VersionId = Guid.Parse("00000000-0000-0000-0000-000000000011") } };
        public WorkflowState WorkflowState { get => _state; set { _state = value; Workflow.InitialStatus = value.Status; } } private WorkflowState _state = new() { PackageId = Package, Status = WorkflowStatus.Draft, Revision = 1 };
        public SubmissionRequest Request { get; set; } = new(Package) { StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"), VerificationSessionId = Guid.Parse("00000000-0000-0000-0000-000000000013"), ExtractionEvidenceJson = "{\"extracted\":true}", VerificationEvidenceJson = "{\"verified\":true}" };
        public WorkflowService Service => new(Workflow, Auth, new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }, new RoleAuthorization(), Logger, new FakeDocuments(this), null, () => Now);
        public static Fixture Create() { var f = new Fixture(); f.Workflow.StateProvider = () => f._state; return f; }
        private sealed class FakeDocuments(Fixture owner) : IDocumentRepository { public Task<Document?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(owner.Document); }
    }
    private sealed class FakeAuth : ISessionAuthenticationService { public AuthenticatedUser? User { get; set; } = CreateMaker(); private static AuthenticatedUser CreateMaker() => new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" }; public AuthenticatedUser? CurrentUser => User; public bool IsAuthenticated => User is not null; public void SignIn(AuthenticatedUser user) => User = user; public void SignOut() => User = null; }
    private sealed class FakeLogger : ICentralizedLogger { public string Context { get; private set; } = ""; public void Log(Exception exception, string operation, string context) => Context = context; }
    private sealed class FakeWorkflow : IWorkflowRepository { public Func<WorkflowState>? StateProvider { get; set; } public WorkflowStatus InitialStatus { get; set; } = WorkflowStatus.Draft; public bool ThrowOnSubmit { get; set; } public Guid? SupersededSubmissionId { get; private set; } public List<ReviewSubmission> Submissions { get; } = new(); public List<AuditEntry> Audits { get; } = new(); public Task<WorkflowState?> GetStateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WorkflowState?>(StateProvider?.Invoke()); public Task<ReviewSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReviewSubmission?>(Submissions.FirstOrDefault(x => x.SubmissionId == id)); public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Submissions); public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>()); public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState s, ReviewSubmission sub, AuditEntry a, CancellationToken ct = default) { if (ThrowOnSubmit) throw new InvalidOperationException("persist failed"); Submissions.Add(sub); Audits.Add(a); return Task.FromResult(sub); } public Task RecordAuditAsync(AuditEntry a, CancellationToken ct = default) { Audits.Add(a); return Task.CompletedTask; } public Task MarkSupersededAsync(Guid id, AuditEntry a, CancellationToken ct = default) { SupersededSubmissionId = id; return Task.CompletedTask; } }
}
