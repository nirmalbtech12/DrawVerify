using DrawVerify.Core.Enums;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class SignatureStampMetadataTests
{
    [Fact(DisplayName = "TSD-04 WorkflowService composes and persists captured stamp metadata")]
    public async Task WorkflowService_signing_composes_custom_fields_and_reloads_integrity()
    {
        var packageId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var mapping = new TemplateMappingSnapshot
        {
            TemplateId = Guid.NewGuid(), TemplateVersion = 3,
            Assignments = new[] { new SignaturePlaceholderAssignment { PlaceholderId = placeholderId, Role = "Checker", AssignedSignerUserId = "checker-1" } },
            CustomFieldMappings = new Dictionary<string, string> { ["Department"] = "Quality" }
        };
        var maker = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", packageId, 1, timestamp);
        var submission = new ReviewSubmission
        {
            PackageId = packageId, Revision = 1, Status = WorkflowStatus.ApprovedByChecker,
            MakerUserId = "maker-1", MakerDisplayName = "Maker One", MakerSignature = maker,
            Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 },
            TemplateMapping = mapping, PlaceholderAssignments = mapping.Assignments
        };
        var repository = new SigningRepository(ReviewPackage.FromSubmission(submission, new ReviewDecision { PackageId = packageId, Revision = 1, DecisionType = ReviewDecisionType.Approve, IsFinalApproval = false }));
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-1", DisplayName = "Checker One" });
        var service = new WorkflowService(repository, authentication, ValidConfiguration(), clock: () => timestamp.UtcDateTime);

        var produced = await service.ApplyCheckerSignatureAsync(packageId, 1, SignatureMethod.Typed, "checker", "Lead", "Quality", placeholderId: placeholderId);
        var reloaded = (await repository.GetReviewPackageAsync(packageId, 1))!.Submission.CheckerSignature;
        Assert.NotNull(reloaded);
        Assert.Equal("Checker One", produced.SignerDisplayName);
        Assert.Equal("Checker", produced.Role);
        Assert.Equal(timestamp, produced.TimestampUtc);
        Assert.Equal(1, produced.Revision);
        Assert.Equal(placeholderId, produced.PlaceholderId);
        Assert.Equal("Quality", produced.CustomFields["Department"]);
        Assert.Equal(produced.CustomFields, reloaded!.CustomFields);
        Assert.Equal(produced.IntegrityHash, reloaded.IntegrityHash);
        Assert.True(reloaded.IsTamperEvident);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyCheckerSignatureAsync(packageId, 1, SignatureMethod.Typed, "again", placeholderId: placeholderId));
        Assert.Equal(produced, (await repository.GetReviewPackageAsync(packageId, 1))!.Submission.CheckerSignature);
    }

    [Fact(DisplayName = "TSD-04 stamp metadata contains signer role timestamp revision placeholder and custom fields")]
    public void Stamp_metadata_is_populated_from_signing_context()
    {
        var placeholderId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var signature = SignatureData.CreateApprover(SignatureMethod.Typed, "approval", "approver-1", "Approver One", Guid.NewGuid(), 4, timestamp, placeholderId: placeholderId, customFields: new Dictionary<string, string> { ["Department"] = "Quality" });

        Assert.Equal("Approver One", signature.SignerDisplayName);
        Assert.Equal("Approver", signature.Role);
        Assert.Equal(timestamp, signature.TimestampUtc);
        Assert.Equal(4, signature.Revision);
        Assert.Equal(placeholderId, signature.PlaceholderId);
        Assert.Equal("Quality", signature.CustomFields["Department"]);
        Assert.True(signature.IsTamperEvident);
    }

    [Fact(DisplayName = "TSD-04 missing required stamp identity fails")]
    public void Missing_document_or_revision_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", Guid.Empty, 1, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", Guid.NewGuid(), 0, DateTimeOffset.UtcNow));
    }

    private static WorkflowConfiguration ValidConfiguration() => new()
    {
        PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
        DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved }
    };

    private sealed class SigningRepository(ReviewPackage initial) : IWorkflowRepository
    {
        private ReviewPackage _package = initial;
        public Task<ReviewPackage?> GetReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default) => Task.FromResult<ReviewPackage?>(_package);
        public Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = packageId, Revision = 1, Status = _package.Status });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewSubmission?>(_package.Submission);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(new[] { _package.Submission });
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.FromResult(submission);
        public Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReviewSubmission> SaveCheckerSignatureAsync(ReviewPackage package, SignatureData signature, DocumentFingerprint fingerprint, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        {
            _package = _package with { Submission = _package.Submission with { CheckerSignature = signature, Fingerprint = fingerprint, PlaceholderAssignments = _package.Submission.PlaceholderAssignments.Select(x => x.PlaceholderId == signature.PlaceholderId ? x with { IsSigned = true, SignedByUserId = signature.SignerUserId, SignedUtc = signature.TimestampUtc } : x).ToArray() } };
            return Task.FromResult(_package.Submission);
        }
    }
}
