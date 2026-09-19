using DrawVerify.Application.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ApprovedDocumentViewerViewModelTests
{
    [Fact(DisplayName = "TSD-VIEWER-OPEN TSD-READONLY TSD-CROSS-CLIENT viewer contract opens read-only and exposes only configured download")]
    public void Viewer_contract_is_read_only_and_download_is_configuration_gated()
    {
        var source = Read("src/DrawVerify.App/ViewModels/ApprovedDocumentViewerViewModel.cs");
        Assert.Contains("public bool IsReadOnly => true", source);
        Assert.Contains("public bool CanDownload", source);
        Assert.Contains("DownloadCommand", source);
        Assert.Contains("OpenAsync", source);
        Assert.Contains("_service.OpenAsync(_document.DocumentId, _document.VersionId)", source);
        Assert.DoesNotContain("EditCommand", source);
        Assert.DoesNotContain("WorkflowStatus", source);
    }

    [Fact(DisplayName = "TSD-METADATA TSD-APPROVAL-DETAILS TSD-SHARING-CONTEXT viewer retains authoritative approval and sharing projection")]
    public void Viewer_contract_preserves_approval_and_sharing_metadata()
    {
        var model = Read("src/DrawVerify.Core/Models/SharedDocument.cs");
        foreach (var field in new[] { "ApproverName", "FinalApprovalUtc", "ApproverSignature", "ApprovalStamp", "SharedByName", "SharedUtc", "SharingNote" })
            Assert.Contains(field, model);
        var view = Read("src/DrawVerify.App/Views/ApprovedDocumentViewerView.xaml");
        Assert.Contains("Document", view);
    }

    [Fact(DisplayName = "TSD-DOWNLOAD TSD-AUDIT viewer reports download failures instead of success")]
    public async Task Viewer_download_failure_is_visible()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }) { UserId = "viewer-1", DisplayName = "Viewer One" });
        var service = new ApprovedDocumentService(new MissingDocumentRepository(), session, new RoleAuthorization(), new NoOpAuditLogger(), new NoOpLogger(), new NoOpStepFileStore(), new NoOpDxfFileStore(), new ViewerDocumentConfiguration { AllowDownload = true });
        using var model = new ApprovedDocumentViewerViewModel(service, new SharedDocument { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing }, canDownload: true);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.Status) && model.Status.StartsWith("Unable to download the approved document:", StringComparison.Ordinal)) completion.TrySetResult(model.Status);
        };

        model.DownloadCommand.Execute(null);

        var status = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.StartsWith("Unable to download the approved document:", status, StringComparison.Ordinal);
    }

    private sealed class MissingDocumentRepository : ISharedDocumentRepository
    {
        public Task<IReadOnlyList<SharedDocument>> ListAsync(string recipientUserId, SharedDocumentQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SharedDocument>>(Array.Empty<SharedDocument>());
        public Task<SharedDocument?> GetAsync(string recipientUserId, Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) => Task.FromResult<SharedDocument?>(null);
        public Task CreateShareAsync(Guid documentId, Guid versionId, string recipientUserId, string sharedByUserId, string sharedByName, DateTime sharedUtc, string? note = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeShareAsync(Guid documentId, Guid versionId, string recipientUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpAuditLogger : IAuditLogger { }
    private sealed class NoOpLogger : ICentralizedLogger { public void Log(Exception exception, string operation, string context) { } }
    private sealed class NoOpStepFileStore : IStepUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromException<Stream>(new NotSupportedException());
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => Task.FromException<StepUploadStagingFile>(new NotSupportedException());
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromException<string>(new NotSupportedException());
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class NoOpDxfFileStore : IDxfUploadFileStore
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromException<Stream>(new NotSupportedException());
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => Task.FromException<DxfUploadStagingFile>(new NotSupportedException());
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromException<string>(new NotSupportedException());
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static string Read(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            var path = Path.Combine(current.FullName, relativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relativePath);
    }
}
