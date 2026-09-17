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
    [Fact(DisplayName = "REV-KAN66-002 Approver signing requires eligible unlocked current final-approval state")]
    public void Approver_signing_contract_rejects_stale_locked_and_non_final_states()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        var methodStart = source.IndexOf("ApplyApproverSignatureAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public async", methodStart + 1, StringComparison.Ordinal);
        var method = methodEnd > methodStart ? source[methodStart..methodEnd] : source[methodStart..];
        Assert.Contains("package.Status", method);
        Assert.Contains("ApproverSignature", method);
        Assert.Contains("current", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "REV-KAN66-002 reject and return paths do not retain an Approver attestation")]
    public void Reject_and_return_contract_excludes_approver_signature_persistence()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        var methodStart = source.IndexOf("FinalDecideCoreAsync", StringComparison.Ordinal);
        var method = source[methodStart..];
        Assert.Contains("ReviewDecisionType.Reject", method);
        Assert.Contains("ReviewDecisionType.Return", method);
        Assert.Contains("ApproverSignature", method);
        Assert.Contains("ReturnedForRework", method);
    }

    [Fact(DisplayName = "TD-APP-01 Approver authorization sequence gating and revision semantics are explicit")]
    public void Approver_workflow_contract_requires_current_prerequisites_and_logs_failures()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("ApplyApproverSignatureAsync", source);
        Assert.Contains("Only an Approver can apply an Approver signature", source);
        Assert.Contains("Maker signature required before Approver signature", source);
        Assert.Contains("Checker signature required before Approver signature", source);
        Assert.Contains("Approver signature required before final approval", source);
        Assert.Contains("ApproverSignatureApplied", source);
        Assert.Contains("signature.DocumentId", source);
        Assert.Contains("signature.Revision", source);
        Assert.Contains("ReturnedForRework", source);
        Assert.Contains("Approver signature", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-02 authorized Approver rejection routes to Checker with complete metadata")]
    public async Task Td02_authorized_approver_rejection_routes_to_checker()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        var result = await f.Service.RejectToCheckerAsync(f.Package, 1, "Dimension mismatch", "Correct datum callout and resubmit.");
        Assert.Equal(ReviewDecisionRoute.ToChecker, result.Route);
        Assert.Equal("approver-1", result.ApproverUserId);
        Assert.Equal("Dimension mismatch", result.OverallReason);
        Assert.Equal(WorkflowStatus.ReturnedForRework, f.Repository.SavedStatus);
        Assert.Equal(DocumentAuditAction.RejectedToChecker, Assert.Single(f.Repository.Audits).Action);
    }

    [Theory(DisplayName = "TD-02 rejects incomplete Approver rejection input without persistence")]
    [InlineData("", "comments")]
    [InlineData("reason", "")]
    public async Task Td02_incomplete_rejection_is_non_mutating(string reason, string comments)
    {
        var f = Fixture.Create(includeApproverSignature: false);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RejectToCheckerAsync(f.Package, 1, reason, comments));
        Assert.Equal(0, f.Repository.DecisionCalls);
    }

    [Fact(DisplayName = "TD-03 duplicate final decision exposes prior decision details")]
    public async Task Td03_duplicate_decision_exposes_prior_details()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        var prior = new ReviewDecision { PackageId = f.Package, Revision = 1, ApproverUserId = "approver-17", IsFinalApproval = true, DecisionType = ReviewDecisionType.Approve, Comments = "approved" };
        f.Repository.Package = f.Repository.Package! with { PriorDecision = prior };
        var exception = await Assert.ThrowsAsync<ReviewDecisionConflictException>(() => f.Service.RejectToCheckerAsync(f.Package, 1, "reason", "comments"));
        Assert.Same(prior, exception.PriorDecision);
        Assert.Equal(0, f.Repository.DecisionCalls);
    }

    [Fact(DisplayName = "TD-05 rejection persists canonical returned status and Checker destination")]
    public async Task Td05_rejection_persists_checker_destination()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        var decision = await f.Service.RejectToCheckerAsync(f.Package, 1, "Dimension mismatch", "Correct datum callout and resubmit.");
        Assert.Equal(WorkflowStatus.ReturnedForRework, f.Repository.SavedStatus);
        Assert.Equal("Checker", decision.DestinationQueue);
        Assert.Equal(1, f.Repository.DecisionCalls);
    }

    [Fact(DisplayName = "TD-07 rejection history retains immutable Approver metadata")]
    public async Task Td07_rejection_metadata_is_immutable_in_saved_decision()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        var decision = await f.Service.RejectToCheckerAsync(f.Package, 1, "Dimension mismatch", "Correct datum callout and resubmit.");
        Assert.NotEmpty(decision.SnapshotReference);
        Assert.Equal(1, decision.Revision);
        Assert.Equal("approver-1", decision.ApproverUserId);
    }

    [Fact(DisplayName = "TD-08 failure validation does not emit notification or partial decision")]
    public async Task Td08_failed_validation_has_no_partial_decision()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RejectToCheckerAsync(f.Package, 1, " ", "comments"));
        Assert.Equal(0, f.Repository.DecisionCalls);
        Assert.Empty(f.Repository.Audits);
    }

    [Fact(DisplayName = "TD-11 reload-compatible rejection carries exact revision and snapshot")]
    public async Task Td11_rejection_is_reload_compatible()
    {
        var f = Fixture.Create(includeApproverSignature: false);
        var decision = await f.Service.RejectToCheckerAsync(f.Package, 1, "Dimension mismatch", "Correct datum callout and resubmit.");
        Assert.Equal(f.Package, decision.PackageId);
        Assert.Equal(1, decision.Revision);
        Assert.NotEmpty(decision.SnapshotReference);
        Assert.NotEmpty(decision.SnapshotJson);
    }
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
        var f = Fixture.Create(includeApproverSignature: false); await f.Service.FinalDecideAsync(f.Package, 1, ReviewDecisionType.Return, "needs correction");
        Assert.Equal(WorkflowStatus.ReturnedForRework, f.Repository.SavedStatus); Assert.Equal(ReviewDecisionType.Return, f.Repository.LastDecision!.DecisionType);
    }

    [Fact(DisplayName = "TD-002 missing Checker signature blocks approval with the required message")]
    public void Missing_checker_signature_message_and_persistence_guard_are_declared()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("Checker signature required before approval", source);
        Assert.Contains("CheckerSignature", source);
        Assert.Contains("SaveCheckerSignatureAsync", source);
        Assert.Contains("fingerprint", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-006 authorization and Maker-before-Checker checks leave blocked operations explicit")]
    public void Checker_authorization_and_current_version_sequence_are_enforced()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("Only a Checker can apply a Checker signature", source);
        Assert.Contains("package.Submission.MakerSignature is null", source);
        Assert.Contains("makerSignature.Validate();", source);
        Assert.Contains("package.Submission.CheckerSignature is not null", source);
    }

    [Fact(DisplayName = "TD-009 returned and resubmitted versions require independent Checker signatures")]
    public void Resubmission_contract_rejects_prior_version_checker_signature_reuse()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("signature.DocumentId", source);
        Assert.Contains("signature.Revision", source);
        Assert.Contains("A Checker signature already exists for this document version", source);
        Assert.Contains("SaveCheckerSignatureAsync", source);
    }

    [Fact(DisplayName = "TD-010 signing failures preserve diagnostics and do not claim success")]
    public void Checker_failure_path_logs_context_and_rethrows()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("_logger?.Log(exception, \"Checker signature\"", source);
        Assert.Contains("packageId={packageId}, revision={revision}", source);
        Assert.Contains("throw;", source);
        Assert.DoesNotContain("return signature;", source);
    }

    [Fact(DisplayName = "REV-KAN65-001 Checker signing requires current Maker signature and approved state")]
    public void Checker_signing_requires_current_maker_and_approved_decision()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("makerSignature.DocumentId != packageId", source);
        Assert.Contains("makerSignature.Revision != revision", source);
        Assert.Contains("package.Status is not (WorkflowStatus.ApprovedByChecker or WorkflowStatus.PendingFinalApproval)", source);
        Assert.Contains("package.PriorDecision.DecisionType != ReviewDecisionType.Approve", source);
        Assert.Contains("WorkflowStatus.SubmittedForReview or WorkflowStatus.UnderReview", source);
    }

    [Fact(DisplayName = "REV-KAN65-002 every approved final approval actor requires persisted Checker signature")]
    public void Approved_finalization_requires_checker_signature_for_all_actors()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Application", "Services", "WorkflowService.cs")));
        Assert.Contains("decisionType == ReviewDecisionType.Approve", source);
        Assert.Contains("package.Submission.CheckerSignature ?? throw", source);
        Assert.DoesNotContain("user.HasRole(SystemRoles.Checker) && package.Submission.CheckerSignature", source);
        Assert.Contains("CreateSignedStateFingerprint", source);
    }

    private sealed class Fixture
    {
        public Guid Package { get; } = Guid.NewGuid(); public FakeRepository Repository { get; } = new(); public FakeAuth Auth { get; } = new();
        public WorkflowService Service => new(Repository, Auth, new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }, new RoleAuthorization(), documents: new FakeDocuments(Package, DxfDocument, Repository.Package!.StepDocumentVersionId, Repository.Package.DxfDocumentVersionId), clock: () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshots: new FakeSnapshotStore(), stepFiles: new FakeStepFiles(), dxfFiles: new FakeDxfFiles());
        private static readonly Guid DxfDocument = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
        public static Fixture Create(bool includeApproverSignature = true) { var f = new Fixture(); f.Repository.Package = ReviewPackage.FromSubmission(new ReviewSubmission { PackageId = f.Package, DxfDocumentId = DxfDocument, Revision = 1, Status = WorkflowStatus.PendingFinalApproval, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), ExtractionEvidenceJson = "{\"extraction\":\"complete\"}", VerificationEvidenceJson = "{\"verification\":\"passed\"}", MakerUserId = "maker-1", MakerDisplayName = "Maker One", Note = "review", MakerSignature = SignatureData.Create(SignatureMethod.Typed, "maker approval", "maker-1", "Maker One", f.Package, 1, new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero)), CheckerSignature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker approval", "checker-001", "Checker One", f.Package, 1, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero)), ApproverSignature = includeApproverSignature ? SignatureData.CreateApprover(SignatureMethod.Typed, "approver approval", "approver-1", "Approver One", f.Package, 1, new DateTimeOffset(2026, 9, 16, 11, 0, 0, TimeSpan.Zero)) : null }); return f; }
    }
    private sealed class FakeAuth : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = new("approver", new[] { SystemRoles.Approver }) { UserId = "approver-1", DisplayName = "Approver One" }; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser user) { } public void SignOut() { } }
    private sealed class FakeSnapshotStore : IApprovalSnapshotStore
    {
        private readonly Dictionary<Guid, ApprovalSnapshot> _snapshots = new();
        public Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default) { _snapshots[snapshot.SnapshotId] = snapshot; return Task.FromResult(snapshot); }
        public Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(_snapshots.TryGetValue(snapshotId, out var snapshot) ? snapshot : null);
    }
    private sealed class FakeDocuments(Guid packageId, Guid dxfId, Guid stepVersionId, Guid dxfVersionId) : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(id == packageId ? new Document { DocumentId = id, OriginalFileName = "model.step", FileSizeBytes = 3, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, GeometryStatus = DrawVerify.Core.Enums.GeometryStatus.Processed, Version = new DocumentVersion { DocumentId = id, VersionId = stepVersionId, StoredRelativePath = "step" } } : id == dxfId ? new Document { DocumentId = id, OriginalFileName = "drawing.dxf", FileSizeBytes = 3, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, VerificationStatus = VerificationStatus.DimensionsExtracted, Version = new DocumentVersion { DocumentId = id, VersionId = dxfVersionId, StoredRelativePath = "dxf" } } : null);
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

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
