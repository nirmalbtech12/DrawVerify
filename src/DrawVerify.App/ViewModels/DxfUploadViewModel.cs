using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Application.Services;

namespace DrawVerify.App.ViewModels;

public sealed class DxfUploadViewModel : INotifyPropertyChanged
{
    public const string PickerFilter = "DXF files (*.dxf)|*.dxf";
    private readonly DxfDocumentService _service;
    private CancellationTokenSource? _cancellation;
    private Stream? _content;
    private string? _fileName;

    public DxfUploadViewModel(DxfDocumentService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        IsAuthorized = service.CanUploadDxf;
        SelectCommand = new RelayCommand(_ => SelectRequested?.Invoke(), _ => IsAuthorized && !IsBusy);
        UploadCommand = new RelayCommand(async _ => await UploadAsync().ConfigureAwait(true), _ => IsAuthorized && _content is not null && !IsBusy);
        CancelCommand = new RelayCommand(_ => _cancellation?.Cancel(), _ => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SelectRequested;
    public event Action<DrawVerify.Core.Models.Document>? DocumentUploaded;

    public string? FileName
    {
        get => _fileName;
        set { _fileName = value; OnChanged(); }
    }

    public string Message { get; private set; } = string.Empty;
    public double Progress { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsAuthorized { get; }
    public ICommand SelectCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand CancelCommand { get; }

    public Func<string, bool> DuplicateConfirmation { get; set; } = message =>
        System.Windows.MessageBox.Show(message, "Duplicate", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    public void Select(string originalFileName, Stream content)
    {
        _content?.Dispose();
        _content = content ?? throw new ArgumentNullException(nameof(content));
        FileName = originalFileName;
        Message = string.Empty;
        Progress = 0;
        OnChanged(nameof(Message));
        OnChanged(nameof(Progress));
        ((RelayCommand)UploadCommand).Raise();
    }

    private async Task UploadAsync()
    {
        if (_content is null || string.IsNullOrWhiteSpace(FileName))
            return;

        IsBusy = true;
        Progress = 0;
        OnChanged(nameof(IsBusy));
        OnChanged(nameof(Progress));
        _cancellation = new CancellationTokenSource();
        try
        {
            var content = _content;
            var request = new DxfUploadRequest(FileName, content)
            {
                Progress = new Progress<long>(bytes =>
                {
                    Progress = content.Length == 0 ? 100 : bytes * 100d / content.Length;
                    OnChanged(nameof(Progress));
                }),
                CancellationToken = _cancellation.Token
            };

            var result = await _service.PrepareDxfUploadAsync(request).ConfigureAwait(true);
            if (result.IsDuplicate)
            {
                if (DuplicateConfirmation(result.Message))
                    result = await _service.UploadDxfAsync(request, result.Preparation!).ConfigureAwait(true);
                else if (result.Document is not null)
                    result = await _service.CancelDuplicateAsync(request, result.Document.DocumentId).ConfigureAwait(true);
            }
            else if (result.Kind == DxfUploadResultKind.Success)
            {
                result = await _service.UploadDxfAsync(request).ConfigureAwait(true);
            }

            Message = result.Message;
            OnChanged(nameof(Message));
            if (result.Kind == DxfUploadResultKind.Success && result.Document is not null)
                DocumentUploaded?.Invoke(result.Document);
        }
        catch (OperationCanceledException)
        {
            Message = DxfDocumentService.CancelledMessage;
            OnChanged(nameof(Message));
        }
        catch (Exception exception)
        {
            Message = $"Upload failed: {exception.Message}";
            OnChanged(nameof(Message));
            System.Windows.MessageBox.Show($"Upload failed for '{FileName}'.\n\n{exception}", "Upload failed (Ctrl+C copies details)", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _content?.Dispose();
            _content = null;
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
            OnChanged(nameof(IsBusy));
            ((RelayCommand)UploadCommand).Raise();
            ((RelayCommand)CancelCommand).Raise();
        }
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => execute(parameter);
        public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
