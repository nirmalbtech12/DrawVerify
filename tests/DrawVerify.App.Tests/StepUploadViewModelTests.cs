using DrawVerify.Application.StepUpload;
using DrawVerify.Application.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.OcctService;
using System.Reflection;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class StepUploadViewModelTests
{
    private const string ValidStep = "ISO-10303-21;HEADER;FILE_SCHEMA(('AUTOMOTIVE_DESIGN_CC2'));ENDSEC;DATA;ENDSEC;END-ISO-10303-21;";

    [Fact(DisplayName = "TSD-01 successful STEP upload raises viewer completion and rejected upload does not")]
    public async Task Upload_completion_is_raised_only_for_successful_step_upload()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }));
        var repository = new UploadRepository();
        var service = new DocumentService(repository, new EmptyAudit(), new UploadStore(), new StepUploadConfiguration(), session, authorization: new RoleAuthorization());
        var viewModel = new StepUploadViewModel(service);
        var uploaded = new List<Document>();
        viewModel.DocumentUploaded += uploaded.Add;
        using var validContent = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ValidStep));
        viewModel.Select("part.step", validContent);
        viewModel.UploadCommand.Execute(null);
        await WaitForUploadAsync(viewModel);
        Assert.Single(uploaded);

        using var invalidContent = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not a STEP model"));
        viewModel.Select("invalid.step", invalidContent);
        viewModel.UploadCommand.Execute(null);
        await WaitForUploadAsync(viewModel);
        Assert.Single(uploaded);
        Assert.Contains("STEP", viewModel.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "TSD-03 uploaded model preview completes with or without a drawing counterpart while verification is absent or pending")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Preview_is_independent_of_verification_and_counterpart(bool hasCounterpart, bool verificationPending)
    {
        var service = new PreviewService();
        using var preview = new StepPreviewViewModel(preview: service);
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }));
        var verification = new VerificationViewModel(session, new RoleAuthorization());
        if (hasCounterpart) verification.DxfDocumentId = Guid.NewGuid().ToString();
        if (verificationPending)
            typeof(VerificationViewModel).GetProperty(nameof(VerificationViewModel.IsBusy))!
                .SetValue(verification, true);

        var uploadRepository = new UploadRepository();
        var uploadService = new DocumentService(uploadRepository, new EmptyAudit(), new UploadStore(), new StepUploadConfiguration(), session, authorization: new RoleAuthorization());
        var uploadViewModel = new StepUploadViewModel(uploadService);
        uploadViewModel.DocumentUploaded += document => preview.AddUploadedModel(document.DocumentId, document.OriginalFileName, document.Version!.StoredRelativePath);
        try
        {
            using var content = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ValidStep));
            uploadViewModel.Select("part.step", content);
            uploadViewModel.UploadCommand.Execute(null);
            await WaitForUploadAsync(uploadViewModel);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (preview.IsProcessing || !preview.IsModelLoaded)
                await Task.Delay(10, timeout.Token);

            Assert.True(preview.IsModelLoaded);
            Assert.Equal(verificationPending, verification.IsBusy);
            Assert.Equal(hasCounterpart, !string.IsNullOrWhiteSpace(verification.DxfDocumentId));
        }
        finally
        {
            verification.Dispose();
        }
    }

    [Fact(DisplayName = "T04 STEP selection is single-file and extension constrained")]
    public void Step_picker_contract_accepts_only_step_variants()
    {
        var view = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml");
        var codeBehind = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml.cs");
        var xaml = File.ReadAllText(view);
        var source = File.ReadAllText(codeBehind);
        Assert.Contains("Filter=\"STEP files (*.step;*.stp)|*.step;*.stp\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new OpenFileDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Multiselect = true", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Select STEP file", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN21-WPF-001 exposes supported-format guidance and rejection message binding")]
    public void Step_upload_view_contains_guidance_and_message_binding()
    {
        var path = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml");
        var xaml = File.ReadAllText(path);
        Assert.Contains("Only STEP files (.step, .stp) are allowed. Please ensure the file is a valid STEP format.", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Message}", xaml, StringComparison.Ordinal);
        Assert.Equal("The uploaded file is not a valid STEP file. Please check the file and try again.", StepFileValidator.NotStepMessage);
        Assert.Equal("The uploaded STEP file appears to be invalid or corrupt. Please check the file and try again.", StepFileValidator.InvalidMessage);
        Assert.Equal("The uploaded STEP file appears to be incomplete or corrupt. Please check the file and try again.", StepFileValidator.TruncatedMessage);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Step upload view was not found.");
    }

    private static async Task WaitForUploadAsync(StepUploadViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (viewModel.IsBusy) await Task.Delay(10, timeout.Token);
    }

    private sealed class PreviewService : IStepPreviewService
    {
        public StepPreviewMeshData? ModelMesh { get; private set; }
        public bool LoadModel(string path)
        {
            ModelMesh = new StepPreviewMeshData(
                new[] { new StepPreviewVertex(new StepPreviewPoint(0, 0, 0)), new StepPreviewVertex(new StepPreviewPoint(1, 0, 0)), new StepPreviewVertex(new StepPreviewPoint(0, 1, 0)) },
                new[] { new StepPreviewTriangle(0, 1, 2, 1) });
            return true;
        }
        public StepPreviewHighlight? ResolveHighlight(string? reference) => null;
        public void Dispose() { }
    }

    private sealed class EmptyAudit : IAuditLogger
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class UploadRepository : IDocumentRepository, ITransactionalDocumentRepository
    {
        public Task CreateWithVersionAndAuditAsync(Document document, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddVersionWithAuditAsync(Guid documentId, DocumentVersion version, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class UploadStore : IStepUploadFileStore
    {
        public Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) => Task.FromResult(new StepUploadStagingFile("staging/part.step", new MemoryStream()));
        public Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromResult("uploads/part.step");
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
