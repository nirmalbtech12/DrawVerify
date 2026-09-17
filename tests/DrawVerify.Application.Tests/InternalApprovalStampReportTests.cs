using System.Text;
using System.Text.Json;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampReportTests
{
    [Fact(DisplayName = "TSD-AC06-SIGNATURE-ACTIONS preserves final approval stamp audit metadata")]
    public void TSD_AC06_SIGNATURE_ACTIONS_preserves_stamp_audit_metadata()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.NewGuid());
        Assert.Equal("approver-001", stamp.ApproverUserId);
        Assert.Equal("Approver One", stamp.ApproverDisplayName);
        Assert.Equal("fingerprint-001", stamp.FingerprintReference);
    }

    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T12 exports carry stamp metadata and verification status")]
    public async Task T12_audit_csv_contains_persisted_stamp_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-stamp-export-{Guid.NewGuid():N}.db");
        try
        {
            var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry { DocumentId = Document, UserId = "approver-001", ActorRole = SystemRoles.Approver, DocumentRevision = "1", DocumentVersion = "REV-001", Action = DocumentAuditAction.InternalApprovalStampApplied, TimestampUtc = Applied.UtcDateTime, DetailsJson = JsonSerializer.Serialize(new { stampId = stamp.StampId, stampApproverUserId = stamp.ApproverUserId, stampApproverDisplayName = stamp.ApproverDisplayName, stampApproverRole = stamp.ApproverRole, stampAppliedUtc = stamp.AppliedUtc, stampFingerprintReference = stamp.FingerprintReference, stampIntegrityHash = stamp.IntegrityHash, verificationResult = "Valid" }) });
            var session = new SessionAuthenticationService();
            session.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var csv = await new AuditLogService(repository, session).ExportDocumentCsvAsync(Document);
            Assert.Contains(stamp.StampId.ToString(), csv);
            Assert.Contains(stamp.ApproverUserId, csv);
            Assert.Contains(stamp.ApproverDisplayName, csv);
            Assert.Contains(stamp.ApproverRole, csv);
            Assert.Contains(stamp.FingerprintReference, csv);
            Assert.Contains(stamp.IntegrityHash, csv);
            Assert.Contains("Valid", csv);

            var downloadDocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var downloadVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var downloadPackageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var submission = new ReviewSubmission
            {
                PackageId = downloadPackageId,
                Revision = 1,
                Status = WorkflowStatus.FinallyApproved,
                DxfDocumentId = downloadDocumentId,
                DxfDocumentVersionId = downloadVersionId,
                InternalApprovalStamp = stamp
            };
            var downloadSession = new SessionAuthenticationService();
            downloadSession.SignIn(new AuthenticatedUser("approver-001", new[] { SystemRoles.Approver }));
            var downloadAudit = new DownloadAudit();
            var downloadService = new DxfDrawingGenerationService(
                new DownloadDocuments(downloadDocumentId, downloadVersionId),
                new UnsupportedStepFiles(),
                new DownloadDxfFiles(),
                new UnsupportedProjection(),
                downloadAudit,
                downloadSession,
                new RoleAuthorization(),
                workflow: new DownloadWorkflow(submission));
            await using var downloaded = await downloadService.DownloadAsync(downloadDocumentId, downloadVersionId);
            using var reader = new StreamReader(downloaded);
            var downloadedText = await reader.ReadToEndAsync();
            Assert.Contains("DRAWVERIFY_INTERNAL_APPROVAL_STAMP", downloadedText);
            Assert.Contains(stamp.StampId.ToString(), downloadedText);
            Assert.Contains(stamp.FingerprintReference, downloadedText);
            Assert.Contains(stamp.ApproverDisplayName, downloadedText);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact(DisplayName = "T17 compliance exports include stamp metadata and verification status")]
    public void T17_markdown_report_contains_stamp_projection()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var markdown = new VerificationReport { InternalApprovalStamp = stamp }.ToMarkdown();
        Assert.Contains(stamp.StampId.ToString(), markdown);
        Assert.Contains(stamp.ApproverDisplayName, markdown);
        Assert.Contains(stamp.FingerprintReference, markdown);
        Assert.Contains(stamp.VerificationStatus.ToString(), markdown);
    }

    private sealed class DownloadAudit : IAuditLogger
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnsupportedProjection : IProjectionDrawingService
    {
        public ProjectionDrawing Generate(string filePath, ProjectionDrawingView view, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedStepFiles : IStepUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DownloadDxfFiles : IDxfUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n0\nEOF\n")));
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DownloadDocuments(Guid documentId, Guid versionId) : IDocumentRepository, ITransactionalDocumentRepository
    {
        private readonly DocumentVersion version = new() { DocumentId = documentId, VersionId = versionId, StoredRelativePath = "approved.dxf" };
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(id == documentId ? new Document { DocumentId = documentId, OriginalFileName = "approved.dxf", Version = version, Versions = new[] { version } } : null);
        public Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddVersionWithAuditAsync(Guid id, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DownloadWorkflow(ReviewSubmission submission) : IWorkflowRepository
    {
        public Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = submission.PackageId, Status = submission.Status, Revision = submission.Revision });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewSubmission?>(submission);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(new[] { submission });
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission value, AuditEntry auditEntry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
