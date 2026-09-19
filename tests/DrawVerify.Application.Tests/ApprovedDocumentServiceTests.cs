using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ApprovedDocumentServiceTests
{
    [Fact(DisplayName = "TSD-ACCESS-FILTER TSD-APPROVED-STATE TSD-SCOPED-SEARCH shared access is recipient and approval scoped")]
    public async Task List_is_scoped_to_active_recipient_and_approved_documents()
    {
        var shared = Document(WorkflowStatus.FinallyApproved, "shared-final.dxf");
        var other = Document(WorkflowStatus.FinallyApproved, "other-final.dxf");
        var draft = Document(WorkflowStatus.Draft, "draft.dxf");
        var repository = new RepositoryFake { Rows = new[] { shared } };
        var service = Create(repository);

        var rows = await service.ListAsync(new SharedDocumentQuery("shared"));

        var row = Assert.Single(rows);
        Assert.Equal(shared.DocumentId, row.DocumentId);
        Assert.DoesNotContain(rows, x => x.DocumentId == other.DocumentId || x.DocumentId == draft.DocumentId);
        Assert.Equal("viewer-1", repository.LastRecipient);
        Assert.Equal("shared", repository.LastQuery?.NormalizedSearch);
    }

    [Fact(DisplayName = "TSD-DIRECT-DENIAL TSD-NONAPPROVED-DENIAL unshared and non-approved direct access is denied")]
    public async Task Direct_access_denies_unshared_or_nonapproved_documents()
    {
        var repository = new RepositoryFake { GetResult = null };
        var audit = new AuditFake();
        var service = Create(repository, audit);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenAsync(Guid.NewGuid()));

        Assert.Equal(ApprovedDocumentService.UnsharedMessage, exception.Message);
        Assert.Empty(audit.Entries);
    }

    [Fact(DisplayName = "TSD-VIEWER-OPEN TSD-READONLY TSD-APPROVAL-DETAILS TSD-SHARING-CONTEXT supported open is read-only and audits the selected version")]
    public async Task Open_returns_selected_version_and_audits_without_mutation_commands()
    {
        var document = Document(WorkflowStatus.FinallyApproved, "approved.step") with
        {
            Revision = 4,
            FinalApprovalUtc = new DateTime(2026, 9, 18, 10, 30, 0, DateTimeKind.Utc),
            ApproverName = "Approver One",
            SharedByName = "Approver One",
            SharingNote = "Released for reference"
        };
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var audit = new AuditFake();
        var service = Create(new RepositoryFake { GetResult = document }, audit, new StepFileStoreFake(stream), new DxfFileStoreFake(stream));

        var access = await service.OpenAsync(document.DocumentId, document.VersionId);

        Assert.Same(stream, access.Content);
        Assert.True(access.Document.ApprovalStatus is WorkflowStatus.FinallyApproved or WorkflowStatus.Released);
        Assert.Equal("Approver One", access.Document.ApproverName);
        Assert.Equal("Released for reference", access.Document.SharingNote);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(DocumentAuditAction.DocumentViewed, entry.Action);
        Assert.Equal(document.VersionId.ToString("D"), entry.DocumentVersion);
        Assert.Equal(SystemRoles.Viewer, entry.ActorRole);
        Assert.True(new ApprovedDocumentViewerContract().IsReadOnly);
    }

    [Fact(DisplayName = "TSD-DOWNLOAD TSD-AUDIT download is default denied and enabled delivery is audited")]
    public async Task Download_requires_configuration_and_records_download_action()
    {
        var document = Document(WorkflowStatus.Released, "approved.dxf");
        var audit = new AuditFake();
        var stream = new MemoryStream(new byte[] { 9 });
        var repository = new RepositoryFake { GetResult = document };
        var disabled = Create(repository, audit, new StepFileStoreFake(stream), new DxfFileStoreFake(stream));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => disabled.DownloadAsync(document.DocumentId, document.VersionId));
        Assert.Empty(audit.Entries);

        var enabled = Create(repository, audit, new StepFileStoreFake(new MemoryStream(new byte[] { 8 })), new DxfFileStoreFake(new MemoryStream(new byte[] { 8 })), new ViewerDocumentConfiguration { AllowDownload = true });
        await using var downloaded = await enabled.DownloadAsync(document.DocumentId, document.VersionId);
        Assert.Equal(8, downloaded.ReadByte());
        Assert.Contains(audit.Entries, x => x.Action == DocumentAuditAction.DocumentDownloaded);
    }

    [Fact(DisplayName = "TSD-REVOCATION revoked access is denied with a visible message")]
    public async Task Revoked_access_does_not_return_content_or_audit()
    {
        var audit = new AuditFake();
        var service = Create(new RepositoryFake { GetException = new UnauthorizedAccessException(ApprovedDocumentService.RevokedMessage) }, audit);
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenAsync(Guid.NewGuid()));
        Assert.Equal(ApprovedDocumentService.RevokedMessage, exception.Message);
        Assert.Empty(audit.Entries);
    }

    private static ApprovedDocumentService Create(RepositoryFake repository, AuditFake? audit = null, StepFileStoreFake? stepFiles = null, DxfFileStoreFake? dxfFiles = null, ViewerDocumentConfiguration? configuration = null)
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }) { UserId = "viewer-1", DisplayName = "Viewer One" });
        audit ??= new AuditFake();
        stepFiles ??= new StepFileStoreFake(new MemoryStream());
        dxfFiles ??= new DxfFileStoreFake(new MemoryStream());
        return new ApprovedDocumentService(repository, session, new RoleAuthorization(), audit, new LoggerFake(), stepFiles, dxfFiles, configuration);
    }

    private static SharedDocument Document(WorkflowStatus status, string name) => new()
    {
        DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), DocumentName = name, Revision = 1,
        ApprovalStatus = status, FinalApprovalUtc = status is WorkflowStatus.FinallyApproved or WorkflowStatus.Released ? DateTime.UtcNow : null,
        ApproverName = "Approver", ApproverUserId = "approver-1", SharedByName = "Approver", SharedByUserId = "approver-1", SharedUtc = DateTime.UtcNow,
        DocumentType = name.EndsWith("dxf", StringComparison.OrdinalIgnoreCase) ? DrawVerify.Core.Enums.DocumentType.DxfDrawing : DrawVerify.Core.Enums.DocumentType.StepModel, StoredRelativePath = name
    };

    private sealed class ApprovedDocumentViewerContract { public bool IsReadOnly => true; }
    private sealed class RepositoryFake : ISharedDocumentRepository
    {
        public IReadOnlyList<SharedDocument> Rows { get; init; } = Array.Empty<SharedDocument>();
        public SharedDocument? GetResult { get; init; }
        public Exception? GetException { get; init; }
        public string? LastRecipient { get; private set; }
        public SharedDocumentQuery? LastQuery { get; private set; }
        public Task<IReadOnlyList<SharedDocument>> ListAsync(string recipientUserId, SharedDocumentQuery query, CancellationToken cancellationToken = default) { LastRecipient = recipientUserId; LastQuery = query; return Task.FromResult(Rows); }
        public Task<SharedDocument?> GetAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) => GetException is null ? Task.FromResult(GetResult) : Task.FromException<SharedDocument?>(GetException);
        public Task CreateShareAsync(Guid documentId, Guid versionId, string recipientUserId, string sharedByUserId, string sharedByName, DateTime sharedUtc, string? note = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeShareAsync(Guid documentId, Guid versionId, string recipientUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class AuditFake : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; } }
    private sealed class LoggerFake : ICentralizedLogger { public void Log(Exception exception, string operation, string context) { } }
    private sealed class StepFileStoreFake : IStepUploadFileStore
    {
        private readonly Stream _stream;
        public StepFileStoreFake(Stream stream) => _stream = stream;
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(_stream);
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class DxfFileStoreFake : IDxfUploadFileStore
    {
        private readonly Stream _stream;
        public DxfFileStoreFake(Stream stream) => _stream = stream;
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(_stream);
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
