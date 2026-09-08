using DrawVerify.App.ViewModels;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DxfUploadViewModelTests
{
    [Fact(DisplayName = "TD-KAN23-WPF-001 DXF picker and progress bindings are exposed")]
    public void Dxf_picker_and_progress_contract_are_exposed()
    {
        Assert.Equal("DXF files (*.dxf)|*.dxf", DxfUploadViewModel.PickerFilter);
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }));
        var service = new DxfDocumentService(new EmptyRepository(), new EmptyAudit(), new EmptyStore(), new DxfUploadConfiguration(), session);
        var viewModel = new DxfUploadViewModel(service);
        Assert.True(viewModel.IsAuthorized);
        Assert.Equal(0, viewModel.Progress);
        Assert.False(viewModel.IsBusy);
    }

    [Fact(DisplayName = "TD-KAN23-WPF-001 cancellation result uses exact user message")]
    public void Cancellation_message_is_exact()
    {
        Assert.Equal("Upload cancelled.", DxfDocumentService.CancelledMessage);
    }

    private sealed class EmptyRepository : IDocumentRepository, ITransactionalDocumentRepository
    {
        public Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class EmptyAudit : IAuditLogger { }
    private sealed class EmptyStore : IDxfUploadFileStore
    { public Task<DxfUploadStagingFile> CreateStagingFileAsync(string n, CancellationToken t = default) => Task.FromResult(new DxfUploadStagingFile("stage", new MemoryStream())); public Task<string> MoveToFinalAsync(DxfUploadStagingFile s, CancellationToken t = default) => Task.FromResult("final.dxf"); public Task DeleteAsync(string p, CancellationToken t = default) => Task.CompletedTask; }
}
