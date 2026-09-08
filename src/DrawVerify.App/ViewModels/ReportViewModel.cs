using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;

namespace DrawVerify.App.ViewModels;

public sealed class ReportViewModel : INotifyPropertyChanged
{
    private readonly ReportService _service;
    private string _sessionId = string.Empty;
    private string _status = "Enter a verification session ID to generate a report.";
    private string _content = string.Empty;
    private bool _isBusy;

    public ReportViewModel(ReportService service)
    {
        _service = service;
        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SessionId { get => _sessionId; set { _sessionId = value; Notify(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string ReportContent { get => _content; private set { _content = value; Notify(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); ((RelayCommand)GenerateCommand).Raise(); } }
    public ICommand GenerateCommand { get; }

    public async Task<bool> GenerateAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(SessionId, out var sessionId)) { ReportContent = string.Empty; Status = "Enter a valid verification session ID."; return false; }
        IsBusy = true;
        try
        {
            ReportContent = await _service.GenerateMarkdownAsync(sessionId, cancellationToken).ConfigureAwait(true);
            Status = "Report generated.";
            return true;
        }
        catch (UnauthorizedAccessException) { ReportContent = string.Empty; Status = "Access denied."; return false; }
        catch (OperationCanceledException) { ReportContent = string.Empty; Status = "Report generation cancelled."; return false; }
        catch (Exception exception) { ReportContent = string.Empty; Status = $"Unable to generate report: {exception.Message}"; return false; }
        finally { IsBusy = false; }
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
        public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
