using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampWorkflowTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T03 final approval persists a version-bound stamp and exact audit action")]
    public async Task T03_stamp_is_created_for_the_current_revision()
    {
        var fixture = Fixture.Create();
        var decision = await fixture.Service.FinalDecideAsync(fixture.PackageId, 1, ReviewDecisionType.Approve, "approved");
        Assert.True(decision.IsFinalApproval);
        Assert.Equal(WorkflowStatus.FinallyApproved, fixture.Repository.Status);
        Assert.NotNull(fixture.Repository.Package!.Submission.InternalApprovalStamp);
        Assert.Equal(1, fixture.Repository.Package.Submission.InternalApprovalStamp!.Revision);
        Assert.Equal(DocumentAuditAction.InternalApprovalStampApplied, Assert.Single(fixture.Repository.Audits).Action);
    }

    [Fact(DisplayName = "T05 re-approval persists distinct historical stamps")]
    public async Task T05_reapproval_creates_distinct_integrity_bound_stamps()
    {
        var fixture = Fixture.Create();
        await fixture.Service.FinalDecideAsync(fixture.PackageId, 1, ReviewDecisionType.Approve, "approved");
        var first = fixture.Repository.Package!.Submission.InternalApprovalStamp!;
        fixture.ReplaceWithRevision(2);
        await fixture.Service.FinalDecideAsync(fixture.PackageId, 2, ReviewDecisionType.Approve, "re-approved");
        var second = fixture.Repository.Package!.Submission.InternalApprovalStamp!;
        Assert.NotEqual(first.StampId, second.StampId);
        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(first, fixture.Repository.History.Single(x => x.Revision == 1).InternalApprovalStamp);
    }

    [Theory(DisplayName = "T06 application service denies final approval to non-Approvers")]
    [InlineData(SystemRoles.Maker)]
    [InlineData(SystemRoles.Checker)]
    [InlineData(SystemRoles.Viewer)]
    public async Task T06_only_approvers_have_final_approval_permission(string role)
    {
        var fixture = Fixture.Create(role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.FinalDecideAsync(fixture.PackageId, 1, ReviewDecisionType.Approve, null));
        Assert.Empty(fixture.Repository.Audits);
        Assert.Null(fixture.Repository.Package!.Submission.InternalApprovalStamp);
    }

    [Fact(DisplayName = "T07 application service rejects a mismatched prerequisite signature")]
    public async Task T07_mismatched_revision_signature_is_not_accepted_as_current()
    {
        var fixture = Fixture.Create();
        fixture.Repository.Package = fixture.Repository.Package! with { Submission = fixture.Repository.Package.Submission with { CheckerSignature = fixture.Repository.Package.Submission.CheckerSignature! with { Revision = 2 } } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.FinalDecideAsync(fixture.PackageId, 1, ReviewDecisionType.Approve, null));
        Assert.Null(fixture.Repository.Package.Submission.InternalApprovalStamp);
        Assert.Empty(fixture.Repository.Audits);
    }

    [Fact(DisplayName = "T08 stamp persistence failure rolls back final approval and logs context")]
    public async Task T08_invalid_stamp_input_fails_without_a_stamp()
    {
        var logger = new CapturingLogger();
        var fixture = Fixture.Create(SystemRoles.Approver, true, logger);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.FinalDecideAsync(fixture.PackageId, 1, ReviewDecisionType.Approve, null));
        Assert.Contains("Failed to apply approval stamp", exception.Message, StringComparison.Ordinal);
        Assert.Equal(WorkflowStatus.PendingFinalApproval, fixture.Repository.Status);
        Assert.Null(fixture.Repository.Package!.Submission.InternalApprovalStamp);
        Assert.Contains(logger.Entries, x => x.Operation == "Internal approval stamp" && x.Context.Contains(fixture.PackageId.ToString(), StringComparison.Ordinal));
    }

    [Theory(DisplayName = "T16 VerifyInternalApprovalStampAsync checks stored revision integrity")]
    [InlineData(SystemRoles.Approver, FingerprintVerificationStatus.Match, InternalApprovalStampVerificationStatus.Valid)]
    [InlineData(SystemRoles.Checker, FingerprintVerificationStatus.Mismatch, InternalApprovalStampVerificationStatus.Invalid)]
    public async Task T16_supported_verifiers_are_authorized_and_maker_is_denied(string role, FingerprintVerificationStatus status, InternalApprovalStampVerificationStatus expected)
    {
        var fixture = Fixture.Create(role, fingerprintStatus: status);
        var stamp = InternalApprovalStamp.Create(fixture.PackageId, 1, "approver-001", "Approver One", Applied, "fingerprint-001");
        var submission = fixture.Repository.Package!.Submission with { InternalApprovalStamp = stamp };
        if (status == FingerprintVerificationStatus.Match)
        {
            var value = new string('a', 64);
            value = SignatureData.ComputePayloadHash($"{value}|{submission.CheckerSignature!.CanonicalRepresentation}");
            value = SignatureData.ComputePayloadHash($"{value}|{submission.ApproverSignature!.CanonicalRepresentation}");
            value = SignatureData.ComputePayloadHash($"{value}|{stamp.CanonicalRepresentation}");
            submission = submission with { Fingerprint = new DocumentFingerprint { Value = value, Algorithm = "SHA-256", CanonicalizationVersion = 1 } };
        }
        fixture.Repository.Package = fixture.Repository.Package with { Submission = submission };
        var result = await fixture.Service.VerifyInternalApprovalStampAsync(fixture.PackageId, 1);
        Assert.Equal(expected, result);
        Assert.Contains(fixture.Repository.Audits, x => x.Action is DocumentAuditAction.VerificationRunCompleted or DocumentAuditAction.IntegrityCheckFailed);
        var maker = Fixture.Create(SystemRoles.Maker);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => maker.Service.VerifyInternalApprovalStampAsync(maker.PackageId, 1));
    }

    private sealed class Fixture
    {
        private static readonly Guid DxfDocument = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid StepVersion = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid DxfVersion = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public Guid PackageId { get; } = Guid.NewGuid();
        public FakeRepository Repository { get; } = new();
        public WorkflowService Service { get; }
        private Fixture(string role, bool failSave, CapturingLogger? logger, FingerprintVerificationStatus status)
        {
            var authentication = new FakeAuthentication(role);
            Repository.Package = ReviewPackage.FromSubmission(CreateSubmission(PackageId, 1));
            Repository.FingerprintStatus = status;
            Repository.FailSave = failSave;
            Service = new WorkflowService(Repository, authentication, new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } }, new RoleAuthorization(), logger, new FakeDocuments(PackageId, Repository.Package.Submission.StepDocumentVersionId, Repository.Package.Submission.DxfDocumentId, Repository.Package.Submission.DxfDocumentVersionId), clock: () => Applied.UtcDateTime, snapshots: new FakeSnapshots(), stepFiles: new FakeStepFiles(), dxfFiles: new FakeDxfFiles(), fingerprintService: new FakeFingerprintService(status));
        }
        public static Fixture Create(string role = SystemRoles.Approver, bool failSave = false, CapturingLogger? logger = null, FingerprintVerificationStatus fingerprintStatus = FingerprintVerificationStatus.Match) => new(role, failSave, logger, fingerprintStatus);
        public void ReplaceWithRevision(int revision) { HistoryCopy(); Repository.Package = ReviewPackage.FromSubmission(CreateSubmission(PackageId, revision)); }
        private void HistoryCopy() { Repository.History.Add(Repository.Package!.Submission); }
        private static ReviewSubmission CreateSubmission(Guid id, int revision) => new() { PackageId = id, Revision = revision, Status = WorkflowStatus.PendingFinalApproval, StepDocumentVersionId = StepVersion, DxfDocumentId = DxfDocument, DxfDocumentVersionId = DxfVersion, VerificationSessionId = Guid.NewGuid(), MakerUserId = "maker-001", MakerDisplayName = "Maker One", ExtractionEvidenceJson = "{\"complete\":true}", VerificationEvidenceJson = "{\"complete\":true}", Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }, MakerSignature = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-001", "Maker One", id, revision, Applied.AddHours(-2)), CheckerSignature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker", "checker-001", "Checker One", id, revision, Applied.AddHours(-1)), ApproverSignature = SignatureData.CreateApprover(SignatureMethod.Typed, "approver", "approver-001", "Approver One", id, revision, Applied) };
    }

    private sealed class FakeAuthentication(string role) : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = new AuthenticatedUser("user", new[] { role }) { UserId = role + "-001", DisplayName = role }; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser user) { } public void SignOut() { } }
    private sealed class CapturingLogger : ICentralizedLogger { public List<(string Operation, string Context)> Entries { get; } = new(); public void Log(Exception exception, string operation, string context) => Entries.Add((operation, context)); }
    private sealed class FakeSnapshots : IApprovalSnapshotStore { private readonly Dictionary<Guid, ApprovalSnapshot> items = new(); public Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot s, CancellationToken ct = default) { items[s.SnapshotId] = s; return Task.FromResult(s); } public Task<ApprovalSnapshot?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(items.TryGetValue(id, out var s) ? s : null); public Task DeleteAsync(Guid id, CancellationToken ct = default) { items.Remove(id); return Task.CompletedTask; } }
    private sealed class FakeDocuments(Guid packageId, Guid stepVersion, Guid dxfDocument, Guid dxfVersion) : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid id, CancellationToken ct = default)
        {
            if (id == dxfDocument)
            {
                var version = new DocumentVersion { DocumentId = id, VersionId = dxfVersion, StoredRelativePath = "dxf" };
                return Task.FromResult<Document?>(new Document { DocumentId = id, OriginalFileName = "drawing.dxf", FileSizeBytes = 3, Version = version, Versions = new[] { version } });
            }
            if (id == packageId)
            {
                var version = new DocumentVersion { DocumentId = id, VersionId = stepVersion, StoredRelativePath = "step" };
                return Task.FromResult<Document?>(new Document { DocumentId = id, OriginalFileName = "model.step", FileSizeBytes = 3, Version = version, Versions = new[] { version } });
            }
            return Task.FromResult<Document?>(null);
        }
        public Task<DocumentVersion?> GetVersionAsync(Guid id, Guid version, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(new DocumentVersion { DocumentId = id, VersionId = version, StoredRelativePath = id == packageId ? "step" : "dxf" });
    }
    private sealed class FakeStepFiles : IStepUploadFileStore { public Task<Stream> OpenReadAsync(string p, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })); public Task<StepUploadStagingFile> CreateStagingFileAsync(string n, CancellationToken ct = default) => throw new NotSupportedException(); public Task<string> MoveToFinalAsync(StepUploadStagingFile f, CancellationToken ct = default) => throw new NotSupportedException(); public Task DeleteAsync(string p, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class FakeDxfFiles : IDxfUploadFileStore { public Task<Stream> OpenReadAsync(string p, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 4, 5, 6 })); public Task<DxfUploadStagingFile> CreateStagingFileAsync(string n, CancellationToken ct = default) => throw new NotSupportedException(); public Task<string> MoveToFinalAsync(DxfUploadStagingFile f, CancellationToken ct = default) => throw new NotSupportedException(); public Task DeleteAsync(string p, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class FakeFingerprintService(FingerprintVerificationStatus status) : IDocumentFingerprintService { public Task<DocumentFingerprint> ComputeAsync(DocumentFingerprintContext c, IReadOnlyCollection<DocumentFingerprintArtifact> a, IProgress<long>? p = null, CancellationToken ct = default) => Task.FromResult(new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 }); public Task<FingerprintVerificationResult> VerifyAsync(DocumentFingerprint e, DocumentFingerprintContext c, IReadOnlyCollection<DocumentFingerprintArtifact> a, CancellationToken ct = default) => Task.FromResult(new FingerprintVerificationResult(status, e, status.ToString())); }
    private sealed class FakeRepository : IWorkflowRepository
    {
        public ReviewPackage? Package { get; set; } public WorkflowStatus Status => Package?.Status ?? WorkflowStatus.Draft; public bool FailSave { get; set; } public FingerprintVerificationStatus FingerprintStatus { get; set; } public List<AuditEntry> Audits { get; } = new(); public List<ReviewSubmission> History { get; } = new();
        public Task<ReviewPackage?> GetReviewPackageAsync(Guid id, int revision, CancellationToken ct = default) => Task.FromResult(Package?.Revision == revision ? Package : null);
        public Task<WorkflowState?> GetStateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = id, Status = Status, Revision = Package?.Revision ?? 0 });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Package?.Submission);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(History.Append(Package?.Submission).Where(x => x is not null).Cast<ReviewSubmission>().ToArray());
        public Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage p, ReviewDecision d, WorkflowStatus s, AuditEntry a, CancellationToken ct = default) => throw new InvalidOperationException("stamp required");
        public Task<ReviewDecision> SaveReviewDecisionAsync(ReviewPackage p, ReviewDecision d, WorkflowStatus s, AuditEntry a, InternalApprovalStamp stamp, CancellationToken ct = default) { if (FailSave) throw new IOException("simulated storage failure"); Package = p with { Status = s, Submission = p.Submission with { InternalApprovalStamp = stamp } }; Audits.Add(a); return Task.FromResult(d); }
        public Task RecordAuditAsync(AuditEntry a, CancellationToken ct = default) { Audits.Add(a); return Task.CompletedTask; }
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState s, ReviewSubmission sub, AuditEntry a, CancellationToken ct = default) => Task.FromResult(sub);
        public Task MarkSupersededAsync(Guid id, AuditEntry a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FingerprintVerificationResult?> GetFingerprintVerificationAsync(Guid id, int revision, CancellationToken ct = default) => Task.FromResult<FingerprintVerificationResult?>(null);
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
    }
}
