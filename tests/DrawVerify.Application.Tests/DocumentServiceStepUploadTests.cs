using System.Text;
using DrawVerify.Application.Services;
using DrawVerify.Application.StepUpload;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using DocumentType = DrawVerify.Core.Enums.DocumentType;
using UploadStatus = DrawVerify.Core.Enums.UploadStatus;
using GeometryStatus = DrawVerify.Core.Enums.GeometryStatus;

namespace DrawVerify.Application.Tests;

public sealed class DocumentServiceStepUploadTests
{
    private const string ValidStep = "ISO-10303-21;HEADER;FILE_DESCRIPTION(('test'),'2;1');ENDSEC;END-ISO-10303-21;";

    [Theory]
    [InlineData("part.step")]
    [InlineData("PART.STP")]
    public async Task Maker_uploads_both_step_extensions_with_required_metadata(string name)
    {
        var fakes = new Fakes();
        var request = new StepUploadRequest(name, new MemoryStream(Encoding.UTF8.GetBytes(ValidStep)));
        var result = await fakes.Service.UploadStepAsync(request);

        Assert.Equal(StepUploadResultKind.Success, result.Kind);
        Assert.Equal($"Uploaded {name} ({Encoding.UTF8.GetByteCount(ValidStep)} bytes).", result.Message);
        var document = fakes.Documents.Created;
        Assert.NotNull(document);
        Assert.Equal(name, document.OriginalFileName);
        Assert.Equal(Encoding.UTF8.GetByteCount(ValidStep), document.FileSizeBytes);
        Assert.Equal(DocumentType.StepModel, document.DocumentType);
        Assert.Equal(UploadStatus.Uploaded, document.UploadStatus);
        Assert.Equal(GeometryStatus.PendingValidation, document.GeometryStatus);
        Assert.Equal("maker-1", document.UploaderUserId);
        Assert.Equal("Maker One", document.UploaderDisplayName);
        Assert.True(document.UploadTimestampUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(DocumentAuditAction.Uploaded, Assert.Single(fakes.Audit.Entries).Action);
        Assert.NotNull(document.Version);
        Assert.Equal(1, document.Version!.VersionNumber);
    }

    [Theory]
    [InlineData("part.dxf")]
    [InlineData("part.txt")]
    [InlineData("part.pdf")]
    public async Task Unsupported_extensions_are_rejected_before_any_write(string name)
    {
        var fakes = new Fakes();
        var result = await fakes.Service.UploadStepAsync(new StepUploadRequest(name, new MemoryStream(Encoding.UTF8.GetBytes(ValidStep))));

        Assert.Equal(StepUploadResultKind.Rejected, result.Kind);
        Assert.Equal(DocumentService.TypeMessage, result.Message);
        Assert.Empty(fakes.Documents.Writes);
        Assert.Empty(fakes.Store.Created);
        Assert.Empty(fakes.Audit.Entries);
    }

    [Fact]
    public async Task Size_boundary_is_accepted_and_one_byte_over_is_rejected()
    {
        var configuration = new StepUploadConfiguration { MaxFileSizeMb = 1 };
        var validBytes = Encoding.UTF8.GetBytes("ISO-10303-21;HEADER;END-ISO-10303-21;");
        var boundary = Enumerable.Repeat((byte)' ', 1024 * 1024).ToArray();
        validBytes.CopyTo(boundary, 0);
        var atLimit = new Fakes(configuration);
        var accepted = await atLimit.Service.UploadStepAsync(new StepUploadRequest("limit.step", new MemoryStream(boundary), boundary.Length));
        Assert.Equal(StepUploadResultKind.Success, accepted.Kind);

        var over = new Fakes(configuration);
        var rejected = await over.Service.UploadStepAsync(new StepUploadRequest("over.step", new MemoryStream(new byte[boundary.Length + 1]), boundary.Length + 1));
        Assert.Equal(StepUploadResultKind.Rejected, rejected.Kind);
        Assert.Equal("File exceeds the maximum allowed size of 1 MB.", rejected.Message);
        Assert.Empty(over.Documents.Writes);
    }

    [Theory]
    [InlineData("not a step")]
    [InlineData("ISO-10303-21;END-ISO-10303-21;")]
    [InlineData("HEADER;END-ISO-10303-21;")]
    public async Task Malformed_step_content_is_rejected_without_persistence(string content)
    {
        var fakes = new Fakes();
        var result = await fakes.Service.UploadStepAsync(new StepUploadRequest("broken.step", new MemoryStream(Encoding.UTF8.GetBytes(content))));

        Assert.Equal(StepUploadResultKind.Rejected, result.Kind);
        Assert.Equal(StepFileValidator.InvalidMessage, result.Message);
        Assert.Empty(fakes.Documents.Writes);
        Assert.Empty(fakes.Store.Created);
    }

    [Fact]
    public async Task Duplicate_confirmation_creates_next_version_and_retains_existing_document()
    {
        var existing = new Document { DocumentId = Guid.NewGuid(), OriginalFileName = "part.step", FileSizeBytes = Encoding.UTF8.GetByteCount(ValidStep), CurrentVersion = 1, Versions = new[] { new DocumentVersion { VersionNumber = 1, DocumentId = Guid.NewGuid(), StoredRelativePath = "old.step" } } };
        var fakes = new Fakes { Duplicate = existing };
        var request = new StepUploadRequest("part.step", new MemoryStream(Encoding.UTF8.GetBytes(ValidStep)));
        var prepared = await fakes.Service.PrepareStepUploadAsync(request);
        Assert.Equal(StepUploadResultKind.Duplicate, prepared.Kind);
        Assert.Equal(DocumentService.DuplicateMessage, prepared.Message);
        Assert.Empty(fakes.Store.Created);

        var result = await fakes.Service.UploadStepAsync(request, confirmDuplicate: true);
        Assert.Equal(StepUploadResultKind.Success, result.Kind);
        var version = Assert.Single(fakes.Documents.AddedVersions);
        Assert.Equal(2, version.VersionNumber);
        Assert.Equal(existing.DocumentId, version.DocumentId);
        Assert.DoesNotContain("old.step", fakes.Store.Deleted);
        Assert.Equal(new[] { DocumentAuditAction.DuplicateDetected, DocumentAuditAction.Overwritten }, fakes.Audit.Entries.Select(x => x.Action));
    }

    [Fact]
    public async Task Duplicate_cancellation_creates_no_version_and_audits_cancelled()
    {
        var existing = new Document { DocumentId = Guid.NewGuid(), OriginalFileName = "part.step", FileSizeBytes = Encoding.UTF8.GetByteCount(ValidStep) };
        var fakes = new Fakes { Duplicate = existing };
        var request = new StepUploadRequest("part.step", new MemoryStream(Encoding.UTF8.GetBytes(ValidStep)));
        await fakes.Service.PrepareStepUploadAsync(request);
        var result = await fakes.Service.CancelDuplicateAsync(request, existing.DocumentId);

        Assert.Equal(StepUploadResultKind.Cancelled, result.Kind);
        Assert.Equal(DocumentService.CancelledMessage, result.Message);
        Assert.Empty(fakes.Documents.AddedVersions);
        Assert.Equal(new[] { DocumentAuditAction.DuplicateDetected, DocumentAuditAction.Cancelled }, fakes.Audit.Entries.Select(x => x.Action));
    }

    [Fact]
    public async Task Cancellation_reports_progress_cleans_staging_and_creates_no_document()
    {
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();
        var fakes = new Fakes(cancelAfterFirstWrite: true, cancelUpload: cancellation.Cancel);
        var request = new StepUploadRequest("part.step", new MemoryStream(Encoding.UTF8.GetBytes(ValidStep))) { Progress = progress, CancellationToken = cancellation.Token };
        var result = await fakes.Service.UploadStepAsync(request);

        Assert.Equal(StepUploadResultKind.Cancelled, result.Kind);
        Assert.Equal(DocumentService.CancelledMessage, result.Message);
        Assert.NotEmpty(progress.Values);
        Assert.Empty(fakes.Documents.Writes);
        Assert.Contains(fakes.Audit.Entries, x => x.Action == DocumentAuditAction.Cancelled && x.DocumentId is null);
        Assert.Contains(fakes.Store.Deleted, x => x.Contains("staging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Persistence_failure_compensates_by_deleting_the_completed_file()
    {
        var fakes = new Fakes { ThrowOnCreate = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fakes.Service.UploadStepAsync(new StepUploadRequest("part.step", new MemoryStream(Encoding.UTF8.GetBytes(ValidStep)))));

        Assert.Contains("uploads/step/final_part.step", fakes.Store.Deleted);
        Assert.Empty(fakes.Audit.Entries);
    }

    [Theory]
    [MemberData(nameof(DeniedUsers))]
    public async Task Authorization_is_enforced_before_stream_or_storage(User? user)
    {
        var fakes = new Fakes(user: user, unauthenticated: user is null);
        var result = await fakes.Service.UploadStepAsync(new StepUploadRequest("part.step", new ThrowingStream()));

        Assert.Equal(StepUploadResultKind.Unauthorized, result.Kind);
        Assert.Empty(fakes.Documents.Writes);
        Assert.Empty(fakes.Store.Created);
        Assert.Empty(fakes.Audit.Entries);
    }

    public static IEnumerable<object?[]> DeniedUsers() => new[] { new object?[] { null }, new object?[] { new User { UserId = "reviewer", DisplayName = "Reviewer", Role = Role.Reviewer } } };

    private sealed class Fakes
    {
        public Fakes(StepUploadConfiguration? configuration = null, User? user = null, bool cancelAfterFirstWrite = false, bool unauthenticated = false, Action? cancelUpload = null)
        {
            Auth = new Authentication(unauthenticated ? null : user ?? new User { UserId = "maker-1", DisplayName = "Maker One", Role = Role.Maker });
            Documents = new DocumentRepository(Audit);
            Store = new FileStore { CancelAfterFirstWrite = cancelAfterFirstWrite, CancelUpload = cancelUpload };
            Service = new DocumentService(Documents, Audit, Auth, Store, configuration ?? new StepUploadConfiguration());
        }
        public Document? Duplicate { get => Documents.Duplicate; set => Documents.Duplicate = value; }
        public bool CancelAfterFirstWrite { get; set; }
        public bool ThrowOnCreate { get => Documents.ThrowOnCreate; set => Documents.ThrowOnCreate = value; }
        public DocumentRepository Documents { get; }
        public AuditLogger Audit { get; } = new();
        public Authentication Auth { get; } = new(new User { UserId = "maker-1", DisplayName = "Maker One", Role = Role.Maker });
        public FileStore Store { get; }
        public DocumentService Service { get; }
    }

    private sealed class Authentication(User? user) : IAuthenticationService { public User? CurrentUser { get; } = user; public bool IsAuthenticated => CurrentUser is not null; public bool IsMaker => CurrentUser?.IsMaker == true; }
    private sealed class RecordingProgress : IProgress<long> { public List<long> Values { get; } = new(); public void Report(long value) => Values.Add(value); public int Count => Values.Count; }
    private sealed class AuditLogger : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; } }
    private sealed class DocumentRepository(AuditLogger audit) : IDocumentRepository, ITransactionalDocumentRepository
    {
        public Document? Duplicate { get; set; }
        public Document? Created { get; private set; }
        public List<string> Writes { get; } = new();
        public List<DocumentVersion> AddedVersions { get; } = new();
        public bool ThrowOnCreate { get; set; }
        public Task<Document?> FindByOriginalNameAndSizeAsync(string originalFileName, long fileSizeBytes, CancellationToken cancellationToken = default) => Task.FromResult(Duplicate);
        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(Created);
        public Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default) { if (ThrowOnCreate) throw new InvalidOperationException("persistence failure"); Created = document; Writes.Add("create"); return Task.CompletedTask; }
        public Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default) { AddedVersions.Add(version); Writes.Add("version"); return Task.CompletedTask; }
        public async Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) { await CreateWithVersionAsync(document, version, cancellationToken); audit.Entries.Add(auditEntry); }
        public async Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) { await AddVersionAsync(documentId, version, cancellationToken); audit.Entries.Add(auditEntry); }
    }
    private sealed class FileStore : IStepUploadFileStore
    {
        public bool CancelAfterFirstWrite { get; set; }
        public Action? CancelUpload { get; set; }
        public List<string> Created { get; } = new();
        public List<string> Deleted { get; } = new();
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) { Created.Add(originalFileName); return Task.FromResult(new StepUploadStagingFile("staging/part.upload", new CancellingStream(CancelAfterFirstWrite, CancelUpload))); }
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromResult("uploads/step/final_part.step");
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) { Deleted.Add(relativePath); return Task.CompletedTask; }
    }
    private sealed class CancellingStream(bool cancel, Action? cancelUpload) : MemoryStream
    {
        private bool _flushed;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => await base.WriteAsync(buffer, cancellationToken);
        public override async Task FlushAsync(CancellationToken cancellationToken = default) { await base.FlushAsync(cancellationToken); if (cancel && !_flushed) { _flushed = true; cancelUpload?.Invoke(); throw new OperationCanceledException(cancellationToken); } }
    }
    private sealed class ThrowingStream : MemoryStream { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(new InvalidOperationException("stream must not be read")); }
}
