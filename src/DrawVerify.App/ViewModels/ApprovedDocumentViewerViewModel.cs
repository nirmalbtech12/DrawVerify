using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DrawVerify.Application.Services;
using DrawVerify.App.Views;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App.ViewModels;

public sealed class ApprovedDocumentViewerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ApprovedDocumentService _service;
    private readonly SharedDocument _document;
    private readonly ICentralizedLogger _logger;
    private string _status = string.Empty;
    private IDisposable? _renderContent;
    private string? _temporaryPath;
    private readonly DispatcherTimer _accessTimer;
    private bool _hasAccess = true;
    private readonly EventHandler _accessTickHandler;
    private bool _disposed;
    public SharedDocument Document => _document;
    public Stream? Content { get; private set; }
    public UIElement? ViewerContent { get; private set; }
    public bool IsReadOnly => true;
    public SharedLinkViewerContext? SharedLinkContext { get; }
    public bool CanDownload => _hasAccess && _service.AllowDownload;
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public ICommand DownloadCommand { get; }
    public event Action? AccessRevoked;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ApprovedDocumentViewerViewModel(ApprovedDocumentService service, SharedDocument document, bool canDownload, ICentralizedLogger? logger = null)
    {
        _service = service; _document = document; _logger = logger ?? new CentralizedLogger(); _hasAccess = canDownload || !service.AllowDownload;
        _accessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) }; _accessTickHandler = (_, _) => _ = ValidateAccessAsync(); _accessTimer.Tick += _accessTickHandler; _accessTimer.Start();
        DownloadCommand = new Command(async _ => await DownloadAsync(), _ => CanDownload);
        _ = OpenAsync();
    }

    public ApprovedDocumentViewerViewModel(ApprovedDocumentService service, SharedDocument document, SharedLinkViewerContext context, ICentralizedLogger? logger = null)
        : this(service, document, context.CanDownload, logger) => SharedLinkContext = context;

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
            RaiseDownloadCanExecute();
        }
        catch (Exception exception) { _logger.Log(exception, "Approved document viewer", $"Unable to render document {_document.DocumentId}, version {_document.VersionId}, format={_document.DocumentType}."); HandleFailure(exception, "Unable to render the approved document"); }
    }

    private async Task BuildReadOnlyHostAsync(Stream content)
    {
        _renderContent?.Dispose(); _renderContent = null;
        if (_document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing)
        {
            var preview = new DxfPreviewViewModel(); preview.SetInternalApprovalStamp(_document.ApprovalStamp);
            ViewerContent = new DxfPreviewView { DataContext = preview, IsEnabled = false }; PropertyChanged?.Invoke(this, new(nameof(ViewerContent))); return;
        }
        if (_document.DocumentType != DrawVerify.Core.Enums.DocumentType.StepModel)
            throw new NotSupportedException($"Document format '{_document.DocumentType}' is not supported by the approved viewer.");
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
        catch (Exception exception) { _logger.Log(exception, "Approved document viewer download", $"Unable to download document {_document.DocumentId}, version {_document.VersionId}, format={_document.DocumentType}."); HandleFailure(exception, "Unable to download the approved document"); }
    }

    private async Task ValidateAccessAsync()
    {
        if (!_hasAccess || _disposed) return;
        try { if (!await _service.ValidateAccessAsync(_document.DocumentId, _document.VersionId).ConfigureAwait(true)) RevokeActiveSession(); }
        catch (Exception exception) { _logger.Log(exception, "Approved viewer access check", $"Document={_document.DocumentId}, version={_document.VersionId}."); RevokeActiveSession(); }
    }
    private void RevokeActiveSession()
    {
        _hasAccess = false; StopAccessTimer(); Status = ApprovedDocumentService.RevokedMessage; DisposeRenderContent("revocation");
        if (Content is not null) { try { Content.Dispose(); } catch (Exception exception) { LogCleanupFailure(exception, "stream disposal during revocation"); } Content = null; PropertyChanged?.Invoke(this, new(nameof(Content))); }
        TryDeleteTemporaryFile(); RaiseDownloadCanExecute(); AccessRevoked?.Invoke();
    }
    private void RaiseDownloadCanExecute() => ((Command)DownloadCommand).Raise();
    private void StopAccessTimer() { _accessTimer.Stop(); _accessTimer.Tick -= _accessTickHandler; }
    private void DisposeRenderContent(string operation)
    {
        if (_renderContent is null) return;
        try { _renderContent.Dispose(); }
        catch (Exception exception) { LogCleanupFailure(exception, $"render resource disposal during {operation}"); }
        finally { _renderContent = null; }
    }
    private void LogCleanupFailure(Exception exception, string operation) => _logger.Log(exception, "Approved viewer cleanup", $"Unable to complete {operation} for document {_document.DocumentId}, version {_document.VersionId}.");

    private void HandleFailure(Exception exception, string prefix)
    {
        Status = $"{prefix}: {exception.Message}";
        if (exception is UnauthorizedAccessException && string.Equals(exception.Message, ApprovedDocumentService.RevokedMessage, StringComparison.Ordinal)) AccessRevoked?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _hasAccess = false; StopAccessTimer(); DisposeRenderContent("viewer disposal");
        if (Content is not null) { try { Content.Dispose(); } catch (Exception exception) { LogCleanupFailure(exception, "stream disposal during viewer disposal"); } Content = null; }
        TryDeleteTemporaryFile(); RaiseDownloadCanExecute(); GC.SuppressFinalize(this);
    }
    private void TryDeleteTemporaryFile()
    {
        if (_temporaryPath is null) return;
        var path = _temporaryPath;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) { LogCleanupFailure(exception, $"temporary file deletion for '{path}'"); }
        finally { _temporaryPath = null; }
    }
    private sealed class Command(Func<object?, Task> execute, Predicate<object?> canExecute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(parameter); public async void Execute(object? parameter) => await execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
