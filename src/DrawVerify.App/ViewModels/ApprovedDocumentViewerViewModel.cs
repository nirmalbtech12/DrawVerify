using System.IO;
using System.ComponentModel;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class ApprovedDocumentViewerViewModel : INotifyPropertyChanged
{
    private readonly ApprovedDocumentService _service;
    private readonly SharedDocument _document;
    private string _status = string.Empty;
    public SharedDocument Document => _document;
    public Stream? Content { get; private set; }
    public bool IsReadOnly => true;
    public bool CanDownload { get; }
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public ICommand DownloadCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ApprovedDocumentViewerViewModel(ApprovedDocumentService service, SharedDocument document, bool canDownload) { _service = service; _document = document; CanDownload = canDownload; DownloadCommand = new Command(async _ => await DownloadAsync(), _ => CanDownload); _ = OpenAsync(); }
    private async Task OpenAsync() { try { Content = (await _service.OpenAsync(_document.DocumentId, _document.VersionId)).Content; PropertyChanged?.Invoke(this, new(nameof(Content))); } catch (Exception exception) { Status = exception.Message; } }
    private async Task DownloadAsync() { try { Content?.Dispose(); Content = await _service.DownloadAsync(_document.DocumentId, _document.VersionId); PropertyChanged?.Invoke(this, new(nameof(Content))); Status = "Download ready."; } catch (Exception exception) { Status = exception.Message; } }
    private sealed class Command(Func<object?, Task> execute, Predicate<object?> canExecute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(parameter); public async void Execute(object? parameter) => await execute(parameter); }
}
