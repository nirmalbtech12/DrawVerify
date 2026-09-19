using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.App.Views;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class ApprovedDocumentViewerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ApprovedDocumentService _service;
    private readonly SharedDocument _document;
    private string _status = string.Empty;
    private IDisposable? _renderContent;
    private string? _temporaryPath;
    public SharedDocument Document => _document;
    public Stream? Content { get; private set; }
    public UIElement? ViewerContent { get; private set; }
    public bool IsReadOnly => true;
    public bool CanDownload { get; }
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public ICommand DownloadCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ApprovedDocumentViewerViewModel(ApprovedDocumentService service, SharedDocument document, bool canDownload)
    {
        _service = service; _document = document; CanDownload = canDownload;
        DownloadCommand = new Command(async _ => await DownloadAsync(), _ => CanDownload);
        _ = OpenAsync();
    }

    public async Task OpenAsync()
    {
        try
        {
            var approvalWarning = false;
            if (!string.IsNullOrWhiteSpace(_document.ApprovalInformationError)) { Status = $"Approval information is invalid: {_document.ApprovalInformationError}"; approvalWarning = true; }
            else if (_document.ApproverSignature is null || _document.ApprovalStamp is null) { Status = "Approval information is unavailable for this document revision."; approvalWarning = true; }
            var access = await _service.OpenAsync(_document.DocumentId, _document.VersionId).ConfigureAwait(true);
            await ReplaceContentAsync(access.Content).ConfigureAwait(true);
            await BuildReadOnlyHostAsync(access.Content).ConfigureAwait(true);
            if (!approvalWarning) Status = "Document opened in read-only mode.";
        }
        catch (Exception exception) { Status = $"Unable to render the approved document: {exception.Message}"; }
    }

    private async Task BuildReadOnlyHostAsync(Stream content)
    {
        _renderContent?.Dispose(); _renderContent = null;
        if (_document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing)
        {
            var preview = new DxfPreviewViewModel(); preview.SetInternalApprovalStamp(_document.ApprovalStamp);
            ViewerContent = new DxfPreviewView { DataContext = preview, IsEnabled = false }; PropertyChanged?.Invoke(this, new(nameof(ViewerContent))); return;
        }
        _temporaryPath = Path.Combine(Path.GetTempPath(), $"drawverify-viewer-{Guid.NewGuid():N}.step");
        await using (var file = File.Create(_temporaryPath)) await content.CopyToAsync(file).ConfigureAwait(true);
        var step = new StepPreviewViewModel();
        if (!step.LoadModel(_temporaryPath)) { step.Dispose(); throw new InvalidDataException("The STEP document could not be rendered."); }
        step.SetInternalApprovalStamp(_document.ApprovalStamp); _renderContent = step;
        ViewerContent = new StepPreviewView { DataContext = step, IsEnabled = false }; PropertyChanged?.Invoke(this, new(nameof(ViewerContent))); TryDeleteTemporaryFile();
    }

    private async Task ReplaceContentAsync(Stream replacement)
    {
        if (Content is not null) await Content.DisposeAsync().ConfigureAwait(true);
        Content = replacement; PropertyChanged?.Invoke(this, new(nameof(Content)));
    }

    private async Task DownloadAsync()
    {
        try { var replacement = await _service.DownloadAsync(_document.DocumentId, _document.VersionId).ConfigureAwait(true); await ReplaceContentAsync(replacement).ConfigureAwait(true); Status = "Download ready."; }
        catch (Exception exception) { Status = $"Unable to download the approved document: {exception.Message}"; }
    }

    public void Dispose() { _renderContent?.Dispose(); _renderContent = null; Content?.Dispose(); Content = null; TryDeleteTemporaryFile(); GC.SuppressFinalize(this); }
    private void TryDeleteTemporaryFile() { if (_temporaryPath is null) return; try { if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath); } catch { } _temporaryPath = null; }
    private sealed class Command(Func<object?, Task> execute, Predicate<object?> canExecute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(parameter); public async void Execute(object? parameter) => await execute(parameter); }
}
