using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class ReportViewModel : INotifyPropertyChanged
{
    private readonly ReportService _service;
    private string _sessionId = string.Empty;
    private string _status = "Enter a verification session ID to generate a report.";
    private string _content = string.Empty;
    private bool _isBusy;
    private readonly ICentralizedLogger? _logger;

    public ReportViewModel(ReportService service, ICentralizedLogger? logger = null)
    {
        _service = service; _logger = logger;
        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => !IsBusy);
        GeneratePdfCommand = new RelayCommand(async _ => await GeneratePdfAsync(), _ => !IsBusy);
        ExportMissingDimensionsCommand = new RelayCommand(async _ => await ExportMissingDimensionsAsync(), _ => !IsBusy);
        ExportExtraDimensionsCommand = new RelayCommand(async _ => await ExportExtraDimensionsAsync(), _ => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SessionId { get => _sessionId; set { _sessionId = value; Notify(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string ReportContent { get => _content; private set { _content = value; Notify(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); ((RelayCommand)GenerateCommand).Raise(); ((RelayCommand)GeneratePdfCommand).Raise(); ((RelayCommand)ExportMissingDimensionsCommand).Raise(); ((RelayCommand)ExportExtraDimensionsCommand).Raise(); } }
    public bool MismatchedAndMissingOnly { get; set; }
    public bool GdtOnly { get; set; }
    public ICommand GenerateCommand { get; }
    public ICommand GeneratePdfCommand { get; }
    public ICommand ExportMissingDimensionsCommand { get; }
    public ICommand ExportExtraDimensionsCommand { get; }
    public string CsvContent { get; private set; } = string.Empty;

    public async Task<bool> GeneratePdfAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(SessionId, out var sessionId)) { Status = "Enter a valid verification session ID."; return false; }
        IsBusy = true;
        try
        {
            var bytes = await _service.GeneratePdfAsync(sessionId, new VerificationReportRequest { MismatchedAndMissingOnly = MismatchedAndMissingOnly, GdtOnly = GdtOnly }, cancellationToken).ConfigureAwait(true);
            var dialog = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "verification-report.pdf", OverwritePrompt = true };
            if (dialog.ShowDialog() != true) { Status = "PDF download cancelled."; return false; }
            await using (var file = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None)) await file.WriteAsync(bytes, cancellationToken).ConfigureAwait(true);
            Status = "PDF report downloaded.";
            return true;
        }
        catch (UnauthorizedAccessException) { Status = "Access denied."; return false; }
        catch (OperationCanceledException) { Status = "PDF report generation cancelled."; return false; }
        catch (Exception exception) { _logger?.Log(exception, "Verification PDF report download", $"Unable to download verificationSessionId={sessionId}."); Status = "Unable to download verification report."; return false; }
        finally { IsBusy = false; }
    }

    public async Task<bool> ExportExtraDimensionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(SessionId, out var sessionId)) { Status = "Enter a valid verification session ID."; return false; }
        IsBusy = true;
        try { CsvContent = await _service.GenerateExtraDimensionsCsvAsync(sessionId, cancellationToken).ConfigureAwait(true); Notify(nameof(CsvContent)); Status = "Extra dimensions exported."; return true; }
        catch (UnauthorizedAccessException) { Status = "Access denied."; return false; }
        catch (Exception exception) { _logger?.Log(exception, "Extra dimension CSV export", $"Unable to export verificationSessionId={sessionId}."); Status = "Unable to export extra dimensions."; return false; }
        finally { IsBusy = false; }
    }

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
        catch (Exception) { ReportContent = string.Empty; Status = "Unable to generate verification report."; return false; }
        finally { IsBusy = false; }
    }

    public async Task<bool> ExportMissingDimensionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(SessionId, out var sessionId)) { _logger?.Log(new ArgumentException("The verification session ID is invalid.", nameof(SessionId)), "Missing dimension CSV export", $"Validation failed for sessionId='{SessionId}'."); Status = "Enter a valid verification session ID."; return false; }
        IsBusy = true;
        try { CsvContent = await _service.GenerateMissingDimensionsCsvAsync(sessionId, cancellationToken).ConfigureAwait(true); Notify(nameof(CsvContent)); Status = "Missing dimensions exported."; return true; }
        catch (UnauthorizedAccessException) { Status = "Access denied."; return false; }
        catch (OperationCanceledException exception) { _logger?.Log(exception, "Missing dimension CSV export", $"Export cancelled for verificationSessionId={sessionId}."); Status = "Export cancelled."; return false; }
        catch (Exception exception) { _logger?.Log(exception, "Missing dimension CSV export", $"Unable to export verificationSessionId={sessionId}."); Status = "Unable to export missing dimensions."; return false; }
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
