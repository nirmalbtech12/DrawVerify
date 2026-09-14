using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class FinalApprovalWorkflowServiceTests
{
    [Fact(DisplayName = "T03 final decision persists details and audit context")]
    public async Task T03_final_decision_persists_actor_revision_snapshot_and_audit()
    {
        var f = Fixture.Create();
        var decision = await f.Service.FinalDecideAsync(f.Package, 1, ReviewDecisionType.Approve, null);
        Assert.True(decision.IsFinalApproval); Assert.Equal("approver-1", decision.ApproverUserId); Assert.Equal(1, decision.Revision);
        Assert.Equal(WorkflowStatus.FinallyApproved, f.Repository.SavedStatus); Assert.Single(f.Repository.Audits);
        Assert.Equal(DocumentAuditAction.FinalApproved, f.Repository.Audits[0].Action);
    }

    [Fact(DisplayName = "T05 duplicate final decisions return a conflict with prior details")]
    public async Task T05_duplicate_final_decision_is_rejected()
    {
        var f = Fixture.Create();
        f.Repository.Package = f.Repository.Package! with { PriorDecision = new ReviewDecision { PackageId = f.Package, Revision = 1, ApproverUserId = "approver-1", IsFinalApproval = true, DecisionType = ReviewDecisionType.Approve, Comments = "done" } };
        var ex = await Assert.ThrowsAsync<ReviewDecisionConflictException>(() => f.Service.FinalDecideAsync(f.Package, 1, ReviewDecisionType.Reject, "issue"));
        Assert.NotNull(ex.PriorDecision); Assert.Equal(ReviewDecisionType.Approve, ex.PriorDecision!.DecisionType); Assert.Equal(0, f.Repository.DecisionCalls);
    }

    [Fact(DisplayName = "T06 complete package contains reviewed content and metadata")]
    public async Task T06_complete_package_is_composed()
    {
        var f = Fixture.Create(); var package = await f.Service.OpenReviewPackageAsync(f.Package, 1);
        Assert.NotNull(package); Assert.NotEqual(Guid.Empty, package!.StepDocumentVersionId); Assert.NotEqual(Guid.Empty, package.DxfDocumentVersionId);
        Assert.Contains("extract", package.ExtractionEvidenceJson); Assert.Contains("verification", package.VerificationEvidenceJson); Assert.Equal("Maker One", package.MakerDisplayName); Assert.Equal("review", package.Note);
    }

    [Fact(DisplayName = "T09 and T10 access policy remains explicit")]
    public void T09_T10_roles_are_not_silently_granted_final_approval_or_configuration()
    {
        var auth = new RoleAuthorization();
        Assert.True(auth.IsAllowed(new AuthenticatedUser("v", new[] { SystemRoles.Viewer }), RolePermission.ReadApprovedDocuments));
        Assert.False(auth.IsAllowed(new AuthenticatedUser("x", Array.Empty<string>()), RolePermission.ControlDocumentAccess));
        Assert.False(auth.IsAllowed(new AuthenticatedUser("c", new[] { SystemRoles.Checker }), RolePermission.FinalApproval));
    }

    [Fact(DisplayName = "T13 decision result is placed in the persisted status path")]
    public async Task T13_result_status_is_persisted_before_return()
    {
        var f = Fixture.Create(); await f.Service.FinalDecideAsync(f.Package, 1, ReviewDecisionType.Return, "needs correction");
        Assert.Equal(WorkflowStatus.ReturnedForRework, f.Repository.SavedStatus); Assert.Equal(ReviewDecisionType.Return, f.Repository.LastDecision!.DecisionType);
    }

    private sealed class Fixture
    {
        public Guid Package { get; } = Guid.NewGuid(); public FakeRepository Repository { get; } = new(); public FakeAuth Auth { get; } = new();
        public WorkflowService Service => new(Repository, Auth, new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }, new RoleAuthorization(), clock: () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshots: new FakeSnapshotStore());
        public static Fixture Create() { var f = new Fixture(); f.Repository.Package = ReviewPackage.FromSubmission(new ReviewSubmission { PackageId = f.Package, Revision = 1, Status = WorkflowStatus.PendingFinalApproval, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), ExtractionEvidenceJson = "{\"extraction\":\"complete\"}", VerificationEvidenceJson = "{\"verification\":\"passed\"}", MakerUserId = "maker-1", MakerDisplayName = "Maker One", Note = "review" }); return f; }
    }
    private sealed class FakeAuth : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = new("approver", new[] { SystemRoles.Approver }) { UserId = "approver-1", DisplayName = "Approver One" }; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser user) { } public void SignOut() { } }
    private sealed class FakeSnapshotStore : IApprovalSnapshotStore
    {
        private readonly Dictionary<Guid, ApprovalSnapshot> _snapshots = new();
        public Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default) { _snapshots[snapshot.SnapshotId] = snapshot; return Task.FromResult(snapshot); }
        public Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(_snapshots.TryGetValue(snapshotId, out var snapshot) ? snapshot : null);
    }
    private sealed class FakeRepository : IWorkflowRepository
    {
        public ReviewPackage? Package { get; set; } public WorkflowStatus? SavedStatus { get; private set; } public ReviewDecision? LastDecision { get; private set; } public int DecisionCalls { get; private set; } public List<AuditEntry> Audits { get; } = new();
        public Task<ReviewPackage?> GetReviewPackageAsync(Guid id, int revision, CancellationToken ct = default) => Task.FromResult(Package);
        public Task<WorkflowState?> GetStateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = id, Status = Package?.Status ?? WorkflowStatus.Draft, Revision = 1 });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReviewSubmission?>(Package?.Submission);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Package is null ? Array.Empty<ReviewSubmission>() : new[] { Package.Submission });
        public Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage p, ReviewDecision d, WorkflowStatus s, AuditEntry a, CancellationToken ct = default) { DecisionCalls++; LastDecision = d; SavedStatus = s; Audits.Add(a); return Task.FromResult(d); }
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState s, ReviewSubmission sub, AuditEntry a, CancellationToken ct = default) => Task.FromResult(sub);
        public Task RecordAuditAsync(AuditEntry a, CancellationToken ct = default) { Audits.Add(a); return Task.CompletedTask; }
        public Task MarkSupersededAsync(Guid id, AuditEntry a, CancellationToken ct = default) => Task.CompletedTask;
    }
}
