using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MultipleSignerPlaceholderWorkflowTests
{
    [Fact(DisplayName = "Each configured signer owns one placeholder and completion waits for both")]
    public async Task Multiple_signers_execute_commands_and_complete_only_after_both_sign()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-multiple-approver-{Guid.NewGuid():N}.db");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
            var packageId = Guid.NewGuid();
        var dxfDocumentId = Guid.NewGuid();
        var stepVersionId = Guid.NewGuid();
        var dxfVersionId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var configuration = new WorkflowConfiguration
        {
            PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
            DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved }
        };
        var assignments = new[]
        {
            new SignaturePlaceholderAssignment { PlaceholderId = firstId, Role = "Approver", AssignedSignerUserId = "approver-1" },
            new SignaturePlaceholderAssignment { PlaceholderId = secondId, Role = "Approver", AssignedSignerUserId = "approver-2" }
        };
        var fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };

        try
        {
            var repository = new SqliteWorkflowRepository(path, configuration: configuration);
            var makerSignature = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", packageId, 1, timestamp);
            var submission = new ReviewSubmission
            {
                SubmissionId = Guid.NewGuid(), PackageId = packageId, DxfDocumentId = dxfDocumentId, StepDocumentVersionId = stepVersionId, DxfDocumentVersionId = dxfVersionId, Revision = 1, MakerUserId = "maker-1", MakerDisplayName = "Maker One",
                MakerAuthorized = true, SubmittedUtc = timestamp.UtcDateTime, CreatedUtc = timestamp.UtcDateTime, MakerSignature = makerSignature,
                Fingerprint = fingerprint, PlaceholderAssignments = assignments, TemplateMapping = new TemplateMappingSnapshot { TemplateId = Guid.NewGuid(), TemplateVersion = 1, Assignments = assignments }
            };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = packageId, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(packageId, "maker-1", "Maker", DocumentAuditAction.SubmissionSucceeded, timestamp));

            var package = await repository.GetReviewPackageAsync(packageId, 1);
            Assert.NotNull(package);
            var decision = new ReviewDecision { PackageId = packageId, Revision = 1, CheckerUserId = "checker-1", CheckerDisplayName = "Checker One", DecisionType = ReviewDecisionType.Approve, DecidedUtc = timestamp.UtcDateTime };
            await repository.SaveReviewDecisionAsync(package!, decision, WorkflowStatus.ApprovedByChecker, Audit(packageId, "checker-1", "Checker", DocumentAuditAction.Approved, timestamp));
            package = await repository.GetReviewPackageAsync(packageId, 1);
            var checkerSignature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker", "checker-1", "Checker One", packageId, 1, timestamp);
            await repository.SaveCheckerSignatureAsync(package!, checkerSignature, fingerprint, Audit(packageId, "checker-1", "Checker", DocumentAuditAction.CheckerSignatureApplied, timestamp));

            var authentication = new SessionAuthenticationService();
            authentication.SignIn(new AuthenticatedUser("approver-1", new[] { SystemRoles.Approver }) { UserId = "approver-1", DisplayName = "Approver One" });
            var service = new WorkflowService(repository, authentication, configuration, new RoleAuthorization(), documents: new SnapshotDocuments(packageId, dxfDocumentId, stepVersionId, dxfVersionId), clock: () => timestamp.UtcDateTime, snapshots: new SnapshotStore(), stepFiles: new SnapshotStepFiles(), dxfFiles: new SnapshotDxfFiles());
            await service.ApplyApproverSignatureAsync(packageId, 1, SignatureMethod.Typed, "approval-1", placeholderId: firstId);

            var afterFirst = await repository.GetReviewPackageAsync(packageId, 1);
            Assert.NotNull(afterFirst);
            Assert.True(afterFirst!.Submission.PlaceholderAssignments.Single(x => x.PlaceholderId == firstId).IsSigned);
            Assert.False(afterFirst.Submission.PlaceholderAssignments.Single(x => x.PlaceholderId == secondId).IsSigned);
            var incompleteApproval = await Record.ExceptionAsync(() => service.FinalDecideAsync(packageId, 1, ReviewDecisionType.Approve, null));
            Assert.NotNull(incompleteApproval);

            var beforeDuplicate = afterFirst.Submission.PlaceholderAssignments.ToArray();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyApproverSignatureAsync(packageId, 1, SignatureMethod.Typed, "duplicate", placeholderId: firstId));
            Assert.Equal(beforeDuplicate, (await repository.GetReviewPackageAsync(packageId, 1))!.Submission.PlaceholderAssignments);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyApproverSignatureAsync(packageId, 1, SignatureMethod.Typed, "unknown", placeholderId: Guid.NewGuid()));
            Assert.Equal(beforeDuplicate, (await repository.GetReviewPackageAsync(packageId, 1))!.Submission.PlaceholderAssignments);

            authentication.SignIn(new AuthenticatedUser("approver-2", new[] { SystemRoles.Approver }) { UserId = "approver-2", DisplayName = "Approver Two" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyApproverSignatureAsync(packageId, 1, SignatureMethod.Typed, "wrong-target", placeholderId: firstId));
            Assert.Equal(beforeDuplicate, (await repository.GetReviewPackageAsync(packageId, 1))!.Submission.PlaceholderAssignments);
            await service.ApplyApproverSignatureAsync(packageId, 1, SignatureMethod.Typed, "approval-2", placeholderId: secondId);

            var completed = await service.FinalDecideAsync(packageId, 1, ReviewDecisionType.Approve, null);
            Assert.True(completed.IsFinalApproval);
            var restored = await repository.GetReviewPackageAsync(packageId, 1);
            Assert.NotNull(restored);
            Assert.Equal(WorkflowStatus.FinallyApproved, restored!.Status);
            Assert.Equal("approver-1", restored.Submission.PlaceholderAssignments.Single(x => x.PlaceholderId == firstId).SignedByUserId);
            Assert.Equal("approver-2", restored.Submission.PlaceholderAssignments.Single(x => x.PlaceholderId == secondId).SignedByUserId);
            Assert.All(restored.Submission.PlaceholderAssignments, assignment => Assert.Equal(timestamp, assignment.SignedUtc));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }

    [Fact(DisplayName = "TSD-08 legacy and mapped submissions retain independent signature mappings")]
    public async Task TSD_08_legacy_and_mapped_submission_mappings_are_independent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-mixed-mapping-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var legacyId = Guid.NewGuid();
            var mappedId = Guid.NewGuid();
            var placeholder = new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Approver", AssignedSignerUserId = "approver-1" };
            var mapping = new TemplateMappingSnapshot { TemplateId = Guid.NewGuid(), TemplateVersion = 1, Assignments = new[] { placeholder } };
            var legacy = new ReviewSubmission { SubmissionId = Guid.NewGuid(), PackageId = legacyId, Revision = 1, MakerUserId = "legacy-maker", TemplateMapping = null };
            var mapped = new ReviewSubmission { SubmissionId = Guid.NewGuid(), PackageId = mappedId, Revision = 1, MakerUserId = "mapped-maker", TemplateMapping = mapping, PlaceholderAssignments = mapping.Assignments };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = legacyId, Status = WorkflowStatus.Draft, Revision = 1 }, legacy, Audit(legacyId, "legacy-maker", "Maker", DocumentAuditAction.SubmissionSucceeded, DateTimeOffset.UtcNow));
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = mappedId, Status = WorkflowStatus.Draft, Revision = 1 }, mapped, Audit(mappedId, "mapped-maker", "Maker", DocumentAuditAction.SubmissionSucceeded, DateTimeOffset.UtcNow));

            var legacyReload = await repository.GetReviewPackageAsync(legacyId, 1);
            var mappedReload = await repository.GetReviewPackageAsync(mappedId, 1);

            Assert.Null(legacyReload!.Submission.TemplateMapping);
            Assert.Equal(mapping.TemplateId, mappedReload!.Submission.TemplateMapping!.TemplateId);
            Assert.Equal(placeholder.PlaceholderId, mappedReload.Submission.TemplateMapping.Assignments.Single().PlaceholderId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }

    private static AuditEntry Audit(Guid packageId, string userId, string role, DocumentAuditAction action, DateTimeOffset timestamp) => new()
    {
        DocumentId = packageId, UserId = userId, ActorRole = role, DocumentRevision = "1", Action = action,
        TimestampUtc = timestamp.UtcDateTime, DetailsJson = "{}"
    };

    private sealed class SnapshotStore : IApprovalSnapshotStore
    {
        private readonly Dictionary<Guid, ApprovalSnapshot> _items = new();
        public Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default) { _items[snapshot.SnapshotId] = snapshot; return Task.FromResult(snapshot); }
        public Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(snapshotId, out var value) ? value : null);
    }

    private sealed class SnapshotDocuments(Guid packageId, Guid dxfDocumentId, Guid stepVersionId, Guid dxfVersionId) : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            if (documentId == packageId) return Task.FromResult<Document?>(new Document { DocumentId = packageId, DocumentType = DrawVerify.Core.Enums.DocumentType.StepModel, Version = new DocumentVersion { DocumentId = packageId, VersionId = stepVersionId, StoredRelativePath = "step" } });
            if (documentId == dxfDocumentId) return Task.FromResult<Document?>(new Document { DocumentId = dxfDocumentId, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, Version = new DocumentVersion { DocumentId = dxfDocumentId, VersionId = dxfVersionId, StoredRelativePath = "dxf" } });
            return Task.FromResult<Document?>(null);
        }
    }

    private sealed class SnapshotStepFiles : IStepUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SnapshotDxfFiles : IDxfUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 4, 5, 6 }));
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
