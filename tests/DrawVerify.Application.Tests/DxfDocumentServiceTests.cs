using System.Text;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DxfDocumentServiceTests
{
    private static byte[] Dxf(string body = "") => Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n" + body + "0\nEOF\n");

    [Fact(DisplayName = "TD-KAN23-APP-001 valid and case-insensitive DXF upload creates required metadata")]
    public async Task Valid_upload_creates_required_document_metadata()
    {
        var f = new Fakes();
        var bytes = Dxf("0\nLINE\n");
        var result = await f.Service.UploadDxfAsync(new DxfUploadRequest("drawing.DXF", new MemoryStream(bytes)));
        Assert.Equal(DxfUploadResultKind.Success, result.Kind);
        var document = f.Documents.Created;
        Assert.NotNull(document);
        Assert.Equal("drawing.DXF", document.OriginalFileName);
        Assert.Equal(bytes.Length, document.FileSizeBytes);
        Assert.Equal(DrawVerify.Core.Enums.DocumentType.DxfDrawing, document!.DocumentType);
        Assert.Equal(DrawVerify.Core.Enums.UploadStatus.Uploaded, document.UploadStatus);
        Assert.Equal(VerificationStatus.PendingVerification, document.VerificationStatus);
        Assert.Equal("MAKER-ID", document.UploaderUserId);
        Assert.Equal("Maker", document.UploaderDisplayName);
        Assert.Contains("drawing.DXF", result.Message);
        Assert.Contains(bytes.Length.ToString(), result.Message);
        Assert.Equal(DocumentAuditAction.Uploaded, Assert.Single(f.Audit.Entries).Action);
    }

    [Theory(DisplayName = "TD-KAN23-APP-001 and AC-02 reject non-DXF extensions before persistence")]
    [InlineData("drawing.step")][InlineData("drawing.txt")][InlineData("drawing.pdf")]
    public async Task Non_dxf_extensions_are_rejected(string name)
    {
        var f = new Fakes();
        var result = await f.Service.UploadDxfAsync(new DxfUploadRequest(name, new MemoryStream(Dxf())));
        Assert.Equal(DxfUploadResultKind.Rejected, result.Kind);
        Assert.Equal(DxfFileValidator.TypeMessage, result.Message);
        Assert.Null(f.Documents.Created);
        Assert.Empty(f.Store.Created);
    }

    [Fact(DisplayName = "TD-KAN23-APP-001 AC-03 enforces configured byte boundary")]
    public async Task Size_boundary_is_exact()
    {
        var config = new DxfUploadConfiguration { MaxFileSizeMb = 1 };
        var f = new Fakes(config);
        var boundary = new byte[config.MaxFileSizeBytes];
        Encoding.ASCII.GetBytes("SECTION").CopyTo(boundary, 0);
        Encoding.ASCII.GetBytes("EOF").CopyTo(boundary, boundary.Length - 3);
        Assert.Equal(DxfUploadResultKind.Success, (await f.Service.UploadDxfAsync(new DxfUploadRequest("one.dxf", new MemoryStream(boundary)))).Kind);
        var oversized = new Fakes(config);
        var result = await oversized.Service.UploadDxfAsync(new DxfUploadRequest("large.dxf", new MemoryStream(new byte[config.MaxFileSizeBytes + 1])));
        Assert.Equal(string.Format(DxfFileValidator.SizeMessageFormat, 1), result.Message);
        Assert.Null(oversized.Documents.Created);
    }

    [Theory(DisplayName = "TD-KAN23-APP-001 AC-04 rejects corrupt structure without a document")]
    [InlineData("0\nEOF\n")][InlineData("0\nSECTION\n")]
    public async Task Missing_structural_marker_is_rejected(string content)
    {
        var f = new Fakes();
        var result = await f.Service.UploadDxfAsync(new DxfUploadRequest("bad.dxf", new MemoryStream(Encoding.ASCII.GetBytes(content))));
        Assert.Equal(DxfFileValidator.InvalidMessage, result.Message);
        Assert.Null(f.Documents.Created);
        Assert.Empty(f.Store.Created);
    }

    [Fact(DisplayName = "TD-KAN23-APP-002 authorization duplicate confirmation cancellation progress and audit")]
    public async Task Authorization_duplicate_version_progress_and_cancellation_are_explicit()
    {
        var deniedSession = new SessionAuthenticationService();
        deniedSession.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));
        var denied = new Fakes(session: deniedSession);
        var rejected = await denied.Service.UploadDxfAsync(new DxfUploadRequest("a.dxf", new MemoryStream(Dxf())));
        Assert.Equal(DxfUploadResultKind.Unauthorized, rejected.Kind);
        Assert.Empty(denied.Store.Created);

        var f = new Fakes { Duplicate = new Document { DocumentId = Guid.NewGuid(), OriginalFileName = "a.dxf", FileSizeBytes = Dxf().Length, CurrentVersion = 1, Versions = Array.Empty<DocumentVersion>() } };
        var request = new DxfUploadRequest("a.dxf", new MemoryStream(Dxf()));
        var prepared = await f.Service.PrepareDxfUploadAsync(request);
        Assert.Equal(DxfUploadResultKind.Duplicate, prepared.Kind);
        Assert.Equal(DxfDocumentService.DuplicateMessage, prepared.Message);
        Assert.Equal(DocumentAuditAction.DuplicateDetected, Assert.Single(f.Audit.Entries).Action);
        var progress = new Progress<long>();
        request = new DxfUploadRequest("a.dxf", new MemoryStream(Dxf())) { Progress = progress };
        var cancelled = await f.Service.CancelDuplicateAsync(request, f.Duplicate!.DocumentId);
        Assert.Equal(DxfUploadResultKind.Cancelled, cancelled.Kind);
        Assert.Equal(DxfDocumentService.CancelledMessage, cancelled.Message);
        Assert.Contains(f.Audit.Entries, x => x.Action == DocumentAuditAction.Cancelled);

        var confirmedFakes = new Fakes { Duplicate = f.Duplicate };
        var confirmedRequest = new DxfUploadRequest("a.dxf", new MemoryStream(Dxf()));
        var confirmation = await confirmedFakes.Service.PrepareDxfUploadAsync(confirmedRequest);
        var copied = new List<long>();
        confirmedRequest = new DxfUploadRequest("a.dxf", new MemoryStream(Dxf())) { Progress = new Progress<long>(copied.Add) };
        var versioned = await confirmedFakes.Service.UploadDxfAsync(confirmedRequest, confirmation.Preparation!);
        Assert.Equal(DxfUploadResultKind.Success, versioned.Kind);
        Assert.NotEmpty(copied);
        Assert.Contains(confirmedFakes.Audit.Entries, x => x.Action == DocumentAuditAction.Overwritten);
    }

    private sealed class Fakes
    {
        public Fakes(DxfUploadConfiguration? config = null, ISessionAuthenticationService? session = null)
        { Audit = new(); Documents = new(Audit); Store = new(); session ??= new SessionAuthenticationService(); if (!session.IsAuthenticated) session.SignIn(new AuthenticatedUser("MAKER", new[] { SystemRoles.Maker }) { UserId = "MAKER-ID", DisplayName = "Maker" }); Service = new(Documents, Audit, Store, config ?? new(), session); }
        public readonly AuditFake Audit; public readonly Repository Documents; public readonly StoreFake Store; public DxfDocumentService Service { get; }
        public Document? Duplicate { get => Documents.Duplicate; set => Documents.Duplicate = value; }
    }
    private sealed class AuditFake : IAuditLogger { public List<AuditEntry> Entries { get; } = new(); public Task WriteAsync(AuditEntry entry, CancellationToken t=default) { Entries.Add(entry); return Task.CompletedTask; } }
    private sealed class Repository(AuditFake audit) : IDocumentRepository, ITransactionalDocumentRepository
    { public Document? Duplicate; public Document? Created; public Task<Document?> FindByOriginalNameAndSizeAsync(string n,long s,CancellationToken t=default)=>Task.FromResult(Duplicate); public Task<Document?> GetAsync(Guid id,CancellationToken t=default)=>Task.FromResult(Created); public Task CreateWithVersionAsync(Document d,DocumentVersion v,CancellationToken t=default){Created=d;return Task.CompletedTask;} public Task AddVersionAsync(Guid id,DocumentVersion v,CancellationToken t=default)=>Task.CompletedTask; public Task CreateWithVersionAndAuditAsync(Document d,DocumentVersion v,AuditEntry a,CancellationToken t=default){Created=d;audit.Entries.Add(a);return Task.CompletedTask;} public Task AddVersionWithAuditAsync(Guid id,DocumentVersion v,AuditEntry a,CancellationToken t=default){audit.Entries.Add(a);return Task.CompletedTask;} }
    private sealed class StoreFake : IDxfUploadFileStore
    { public List<string> Created { get; } = new(); public Task<DxfUploadStagingFile> CreateStagingFileAsync(string n,CancellationToken t=default){Created.Add(n);return Task.FromResult(new DxfUploadStagingFile("uploads/dxf/stage",new MemoryStream()));} public Task<string> MoveToFinalAsync(DxfUploadStagingFile s,CancellationToken t=default)=>Task.FromResult("uploads/dxf/final.dxf"); public Task DeleteAsync(string p,CancellationToken t=default)=>Task.CompletedTask; }
}
