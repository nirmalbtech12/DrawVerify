using System.Text;
using DrawVerify.Application.DrawingUpload;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DrawingUploadServiceTests
{
    private static readonly IReadOnlyDictionary<string, byte[]> ValidFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["drawing.pdf"] = Encoding.ASCII.GetBytes("%PDF-1.7 drawing"),
        ["drawing.png"] = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
        ["drawing.jpg"] = new byte[] { 0xff, 0xd8, 0xff, 0xe0 },
        ["drawing.jpeg"] = new byte[] { 0xff, 0xd8, 0xff, 0xe0 },
        ["drawing.tif"] = new byte[] { (byte)'I', (byte)'I', 42, 0 },
        ["drawing.tiff"] = new byte[] { (byte)'M', (byte)'M', 0, 42 }
    };

    [Theory(DisplayName = "TSD-AC01-UPLOAD supported PDF and image files persist as versions")]
    [MemberData(nameof(SupportedFiles))]
    public async Task Supported_files_create_document_and_version_records(string fileName, byte[] bytes)
    {
        var fixture = new Fixture();
        try
        {
            var result = await fixture.Service.UploadDrawingAsync(new DrawingUploadRequest(fileName, new MemoryStream(bytes)));

            Assert.Equal(DrawingUploadResultKind.Success, result.Kind);
            Assert.NotNull(fixture.Documents.Created);
            Assert.NotNull(fixture.Documents.Created!.Version);
            Assert.Equal(fileName, fixture.Documents.Created.OriginalFileName);
            Assert.Equal(1, fixture.Documents.Created.Version!.VersionNumber);
            Assert.Equal(fileName, result.Document!.OriginalFileName);
        }
        finally { fixture.Dispose(); }
    }

    public static IEnumerable<object[]> SupportedFiles() => ValidFiles.Select(x => new object[] { x.Key, x.Value });

    [Theory(DisplayName = "TSD-AC01-UPLOAD invalid drawing input is rejected without persistence")]
    [InlineData("drawing.txt", "not supported")]
    [InlineData("drawing.pdf", "not a pdf")]
    [InlineData("drawing.png", "not a png")]
    public async Task Invalid_files_are_visible_and_leave_no_records(string fileName, string content)
    {
        var fixture = new Fixture();
        try
        {
            var result = await fixture.Service.UploadDrawingAsync(new DrawingUploadRequest(fileName, new MemoryStream(Encoding.ASCII.GetBytes(content))));

            Assert.Equal(DrawingUploadResultKind.Rejected, result.Kind);
            Assert.NotEmpty(result.Message);
            Assert.Null(fixture.Documents.Created);
            Assert.Empty(fixture.Store.Created);
        }
        finally { fixture.Dispose(); }
    }

    [Fact(DisplayName = "TSD-AC01-UPLOAD duplicate uses confirmation and creates the next version")]
    public async Task Duplicate_upload_requires_confirmation_then_versions_existing_document()
    {
        var fixture = new Fixture();
        try
        {
            var bytes = ValidFiles["drawing.pdf"];
            var first = await fixture.Service.UploadDrawingAsync(new DrawingUploadRequest("drawing.pdf", new MemoryStream(bytes)));
            fixture.Documents.Duplicate = first.Document;

            var duplicate = await fixture.Service.UploadDrawingAsync(new DrawingUploadRequest("drawing.pdf", new MemoryStream(bytes)));
            Assert.Equal(DrawingUploadResultKind.Duplicate, duplicate.Kind);

            var confirmed = await fixture.Service.UploadDrawingAsync(new DrawingUploadRequest("drawing.pdf", new MemoryStream(bytes)), confirmDuplicate: true);
            Assert.Equal(DrawingUploadResultKind.Success, confirmed.Kind);
            Assert.Contains(fixture.Documents.Versions, version => version.VersionNumber == 2);
        }
        finally { fixture.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        public AuditLog Audit { get; } = new();
        public DocumentRepository Documents { get; } = new();
        public FileStore Store { get; } = new();
        public DocumentService Service { get; }

        public Fixture()
        {
            var session = new SessionAuthenticationService();
            session.SignIn(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-1", DisplayName = "Maker" });
            Service = new DocumentService(Documents, Audit, Store, new StepUploadConfiguration(), session, workflow: new WorkflowRepository());
        }

        public void Dispose() => Store.Dispose();
    }

    private sealed class AuditLog : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class DocumentRepository : IDocumentRepository, ITransactionalDocumentRepository
    {
        public Document? Created { get; private set; }
        public Document? Duplicate { get; set; }
        public List<DocumentVersion> Versions { get; } = new();
        public Task<Document?> FindByOriginalNameAndSizeAsync(string name, long size, CancellationToken cancellationToken = default) => Task.FromResult(Duplicate ?? (Created?.OriginalFileName == name && Created.FileSizeBytes == size ? Created : null));
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Created?.DocumentId == id ? Created : null);
        public Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default) { Created = document with { Version = version, Versions = new[] { version } }; Versions.Add(version); return Task.CompletedTask; }
        public Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default) { Versions.Add(version); if (Created is not null) Created = Created with { Version = version, CurrentVersion = version.VersionNumber, Versions = Versions.ToArray() }; return Task.CompletedTask; }
        public Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => CreateWithVersionAsync(document, version, cancellationToken);
        public Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => AddVersionAsync(documentId, version, cancellationToken);
    }

    private sealed class FileStore : IStepUploadFileStore, IDisposable
    {
        public List<string> Created { get; } = new();
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string name, CancellationToken cancellationToken = default) { Created.Add(name); return Task.FromResult(new StepUploadStagingFile("staging/" + name, new MemoryStream())); }
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromResult("uploads/" + stagingFile.SanitizedFileName ?? stagingFile.RelativePath);
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
        public void Dispose() { Created.Clear(); }
    }

    private sealed class WorkflowRepository : IWorkflowRepository
    {
        public Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = packageId, Status = WorkflowStatus.Draft });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewSubmission?>(null);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.FromResult(submission);
        public Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
