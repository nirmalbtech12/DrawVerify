using System.Text;
using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Application.StepUpload;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using StepDocumentType = DrawVerify.Core.Enums.DocumentType;
using StepUploadStatus = DrawVerify.Core.Enums.UploadStatus;
using StepGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;

namespace DrawVerify.Application.Tests;

public sealed class DocumentServiceStepUploadTests
{
    private const string Valid = "ISO-10303-21;HEADER;FILE_SCHEMA(('AUTOMOTIVE_DESIGN_CC2'));ENDSEC;DATA;ENDSEC;END-ISO-10303-21;";
    private static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    [Theory(DisplayName = "TD-KAN21-APP-001 rejects non-STEP content and audits it")]
    [InlineData("renamed.step", "renamed text")]
    [InlineData("document.stp", "%PDF-1.7")]
    public async Task Non_step_content_is_rejected(string name, string content)
    {
        var f = new Fakes();
        var r = await f.Service.UploadStepAsync(new(name, new MemoryStream(B(content))));
        Assert.Equal(StepUploadResultKind.Rejected, r.Kind);
        Assert.Equal(StepFileValidator.NotStepMessage, r.Message);
        AssertNoWrites(f); AssertAudit(f, name, "Not a STEP file");
    }

    [Fact(DisplayName = "TD-KAN21-APP-001 rejects image-like binary content")]
    public async Task Image_content_is_rejected()
    {
        var f = new Fakes();
        var r = await f.Service.UploadStepAsync(new("image.step", new MemoryStream(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0, 1 })));
        Assert.Equal(StepFileValidator.NotStepMessage, r.Message); AssertNoWrites(f); AssertAudit(f, "image.step", "Not a STEP file");
    }

    [Theory(DisplayName = "TD-KAN21-APP-002 rejects malformed headers and restores stream position")]
    [InlineData("missing-schema.step", "ISO-10303-21;HEADER;ENDSEC;END-ISO-10303-21;")]
    [InlineData("malformed-schema.stp", "ISO-10303-21;HEADER;FILE_SCHEM(('X'));ENDSEC;END-ISO-10303-21;")]
    public async Task Invalid_headers_are_rejected(string name, string content)
    {
        var f = new Fakes(); using var stream = new MemoryStream(B(content)); stream.Position = 3;
        var r = await f.Service.UploadStepAsync(new(name, stream));
        Assert.Equal(StepFileValidator.InvalidMessage, r.Message); Assert.Equal(3, stream.Position);
        AssertNoWrites(f); AssertAudit(f, name, "Invalid header");
    }

    [Fact(DisplayName = "TD-KAN21-APP-003 classifies truncation, timeout, and read failure")]
    public async Task Invalid_streams_are_safe()
    {
        var f = new Fakes();
        var r = await f.Service.UploadStepAsync(new("truncated.step", new MemoryStream(B("ISO-10303-21;HEADER;FILE_SCHEMA(('X'));ENDSEC;"))));
        Assert.Equal(StepFileValidator.TruncatedMessage, r.Message); AssertAudit(f, "truncated.step", "Truncated file"); AssertNoWrites(f);
        var mismatch = new Fakes();
        r = await mismatch.Service.UploadStepAsync(new("mismatch.step", new MemoryStream(B(Valid)), 1));
        Assert.Equal(StepFileValidator.TruncatedMessage, r.Message); AssertAudit(mismatch, "mismatch.step", "Truncated file");
        var slow = new Fakes(new StepUploadConfiguration { ValidationTimeoutSeconds = 1 });
        r = await slow.Service.UploadStepAsync(new("slow.step", new DelayedStream(B(Valid))));
        Assert.Equal(StepFileValidator.TimeoutMessage, r.Message); AssertAudit(slow, "slow.step", "Validation timeout");
        var failed = new Fakes(logger: new Logger());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Service.UploadStepAsync(new("read-error.step", new ThrowingStream())));
        Assert.Contains(failed.Logger!.Entries, x => x.Contains("STEP upload validation"));
    }

    [Theory(DisplayName = "TD-KAN21-APP-004 authorization precedes processing")]
    [InlineData(null)] [InlineData("Viewer")]
    public async Task Unauthorized_users_do_not_read_or_write(string? role)
    {
        var session = new SessionAuthenticationService();
        if (role is not null) session.SignIn(new AuthenticatedUser("user", new[] { role }));
        var f = new Fakes(session: session, authenticate: false); var stream = new TrackingStream(B("not step"));
        var r = await f.Service.UploadStepAsync(new("bad.step", stream));
        Assert.Equal(StepUploadResultKind.Rejected, r.Kind); Assert.Equal(0, stream.Reads); AssertNoWrites(f);
    }

    [Fact(DisplayName = "TD-KAN21-APP-005 accepts valid surface/wire format and bounded reads")]
    public async Task Valid_format_is_uploaded_without_geometry_validation()
    {
        var f = new Fakes();
        var r = await f.Service.UploadStepAsync(new("surface.stp", new MemoryStream(B(Valid))));
        Assert.True(r.Kind == StepUploadResultKind.Success, $"Unexpected result: {r.Kind}; message: {r.Message}");
        Assert.Equal(StepDocumentType.StepModel, f.Documents.Created!.DocumentType);
        Assert.Equal(StepUploadStatus.Uploaded, f.Documents.Created.UploadStatus);
        Assert.Equal(StepGeometryStatus.PendingValidation, f.Documents.Created.GeometryStatus);
        var bytes = new byte[50 * 1024 * 1024]; B(Valid).CopyTo(bytes, 0); B("END-ISO-10303-21;").CopyTo(bytes, bytes.Length - 17);
        var bounded = new Fakes(); var tracking = new TrackingStream(bytes);
        Assert.Equal(StepUploadResultKind.Success, (await bounded.Service.UploadStepAsync(new("large.step", tracking))).Kind);
        Assert.True(tracking.ValidationBytesRead < bytes.Length, $"Validation read {tracking.ValidationBytesRead} bytes; copy read {tracking.CopyBytesRead} bytes.");
    }

    [Fact(DisplayName = "TD-KAN21-APP-006 preserves duplicate and compensation behavior")]
    public async Task Existing_upload_boundaries_remain_intact()
    {
        var f = new Fakes { Duplicate = new Document { DocumentId = Guid.NewGuid(), OriginalFileName = "part.step", FileSizeBytes = B(Valid).Length } };
        var request = new StepUploadRequest("part.step", new MemoryStream(B(Valid)));
        Assert.Equal(StepUploadResultKind.Duplicate, (await f.Service.PrepareStepUploadAsync(request)).Kind);
        Assert.Equal(StepUploadResultKind.Cancelled, (await f.Service.CancelDuplicateAsync(request, f.Documents.Duplicate!.DocumentId)).Kind);
        var failure = new Fakes { ThrowOnCreate = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => failure.Service.UploadStepAsync(new("part.step", new MemoryStream(B(Valid)))));
        Assert.NotEmpty(failure.Store.Deleted);
    }

    [Fact(DisplayName = "TD-APP-02 opens the exact historical STEP version read-only")]
    public async Task Historical_version_read_uses_exact_version_and_is_disposable()
    {
        var f = new Fakes();
        var documentId = Guid.NewGuid();
        var prior = new DocumentVersion { DocumentId = documentId, VersionId = Guid.NewGuid(), VersionNumber = 1, StoredRelativePath = "history/prior.step" };
        var current = new DocumentVersion { DocumentId = documentId, VersionId = Guid.NewGuid(), VersionNumber = 2, StoredRelativePath = "current.step" };
        f.Documents.Created = new Document { DocumentId = documentId, Status = DrawVerify.Core.Enums.DocumentStatus.Active, Versions = new[] { prior, current }, Version = current };

        await using var stream = await f.Service.OpenHistoricalReadAsync(documentId, prior.VersionId);

        Assert.False(stream.CanWrite);
        Assert.Equal("history/prior.step", f.Store.LastReadPath);
        using var reader = new StreamReader(stream, leaveOpen: true);
        Assert.Equal("historical", await reader.ReadToEndAsync());
    }

    private static void AssertNoWrites(Fakes f) { Assert.Empty(f.Documents.Writes); Assert.Empty(f.Store.Created); }
    private static void AssertAudit(Fakes f, string name, string reason)
    {
        var a = Assert.Single(f.Audit.Entries); Assert.Equal(DocumentAuditAction.InvalidStepFileRejected, a.Action);
        Assert.Null(a.DocumentId); Assert.NotEqual(default, a.TimestampUtc); Assert.Equal("MAKER", a.UserId);
        using var j = JsonDocument.Parse(a.DetailsJson); Assert.Equal(name, j.RootElement.GetProperty("originalFileName").GetString());
        Assert.Equal(reason, j.RootElement.GetProperty("reason").GetString()); Assert.True(j.RootElement.GetProperty("fileSizeBytes").GetInt64() > 0);
    }

    private sealed class Fakes
    {
        public Fakes(StepUploadConfiguration? config = null, ISessionAuthenticationService? session = null, Logger? logger = null, bool authenticate = true)
        { Audit = new(); Documents = new(Audit); Store = new(); Logger = logger; session ??= new SessionAuthenticationService(); if (authenticate && session.CurrentUser is null) session.SignIn(new AuthenticatedUser("MAKER", new[] { SystemRoles.Maker })); Service = new(Documents, Audit, Store, config ?? new(), session, logger, workflow: new WorkflowAccess()); }
        public readonly AuditLog Audit; public readonly DocumentRepository Documents; public readonly FileStore Store; public readonly Logger? Logger; public DocumentService Service { get; }
        public Document? Duplicate { get => Documents.Duplicate; set => Documents.Duplicate = value; } public bool ThrowOnCreate { get => Documents.ThrowOnCreate; set => Documents.ThrowOnCreate = value; }
    }
    private sealed class AuditLog : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public Task WriteAsync(AuditEntry e, CancellationToken t = default) { Entries.Add(e); return Task.CompletedTask; } }
    private sealed class DocumentRepository(AuditLog audit) : IDocumentRepository, ITransactionalDocumentRepository
    {
        public Document? Duplicate { get; set; } public Document? Created { get; set; } public bool ThrowOnCreate { get; set; } public List<string> Writes { get; } = new();
        public Task<Document?> FindByOriginalNameAndSizeAsync(string n, long s, CancellationToken t = default) => Task.FromResult(Duplicate);
        public Task<Document?> GetAsync(Guid id, CancellationToken t = default) => Task.FromResult(Created?.DocumentId == id ? Created : null);
        public Task CreateWithVersionAsync(Document d, DocumentVersion v, CancellationToken t = default) { if (ThrowOnCreate) throw new InvalidOperationException("persistence failure"); Created = d; Writes.Add("create"); return Task.CompletedTask; }
        public Task AddVersionAsync(Guid id, DocumentVersion v, CancellationToken t = default) { Writes.Add("version"); return Task.CompletedTask; }
        public async Task CreateWithVersionAndAuditAsync(Document d, DocumentVersion v, AuditEntry a, CancellationToken t = default) { await CreateWithVersionAsync(d, v, t); audit.Entries.Add(a); }
        public async Task AddVersionWithAuditAsync(Guid id, DocumentVersion v, AuditEntry a, CancellationToken t = default) { await AddVersionAsync(id, v, t); audit.Entries.Add(a); }
    }
    private sealed class FileStore : IStepUploadFileStore { public List<string> Created { get; } = new(); public List<string> Deleted { get; } = new(); public string? LastReadPath { get; private set; } public Task<Stream> OpenReadAsync(string p, CancellationToken t = default) { LastReadPath = p; return Task.FromResult<Stream>(new ReadOnlyMemoryStream(B("historical"))); } public Task<StepUploadStagingFile> CreateStagingFileAsync(string n, CancellationToken t = default) { Created.Add(n); return Task.FromResult(new StepUploadStagingFile("staging/test", new MemoryStream())); } public Task<string> MoveToFinalAsync(StepUploadStagingFile s, CancellationToken t = default) => Task.FromResult("uploads/step/final.step"); public Task DeleteAsync(string p, CancellationToken t = default) { Deleted.Add(p); return Task.CompletedTask; } }
    private sealed class WorkflowAccess : IWorkflowRepository { public Task<WorkflowState?> GetStateAsync(Guid id, CancellationToken t = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = id, Status = WorkflowStatus.Draft }); public Task<ReviewSubmission?> GetSubmissionAsync(Guid id, CancellationToken t = default) => Task.FromResult<ReviewSubmission?>(null); public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid id, CancellationToken t = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>()); public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken t = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>()); public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState s, ReviewSubmission sub, AuditEntry a, CancellationToken t = default) => Task.FromResult(sub); public Task RecordAuditAsync(AuditEntry a, CancellationToken t = default) => Task.CompletedTask; public Task MarkSupersededAsync(Guid id, AuditEntry a, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class ReadOnlyMemoryStream(byte[] data) : MemoryStream(data, writable: false) { }
    private sealed class Logger : ICentralizedLogger { public List<string> Entries { get; } = new(); public void Log(Exception e, string op, string ctx) => Entries.Add(op + ctx + e.Message); }
    private sealed class TrackingStream(byte[] data) : MemoryStream(data) { public int Reads { get; private set; } public long ValidationBytesRead { get; private set; } public long CopyBytesRead { get; private set; } public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken t = default) { Reads++; return ReadTracked(b); } private async ValueTask<int> ReadTracked(Memory<byte> b) { var n = await base.ReadAsync(b); if (b.Length <= 8192) ValidationBytesRead += n; else CopyBytesRead += n; return n; } }
    private sealed class DelayedStream(byte[] data) : MemoryStream(data) { public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken t = default) { await Task.Delay(TimeSpan.FromSeconds(5), t); return await base.ReadAsync(b, t); } }
    private sealed class ThrowingStream : MemoryStream { public ThrowingStream() : base(B("non-empty")) { } public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken t = default) => ValueTask.FromException<int>(new InvalidOperationException("read failure")); }
}
