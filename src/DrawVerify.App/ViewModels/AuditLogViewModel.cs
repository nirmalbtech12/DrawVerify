using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Models;
namespace DrawVerify.App.ViewModels;
public sealed class AuditLogViewModel : INotifyPropertyChanged
{
    private readonly AuditLogService _service;
    public AuditLogViewModel(AuditLogService service) { _service = service; QueryCommand = new Command(async _ => { if (DocumentId != Guid.Empty) await LoadDocumentAsync(); else await LoadAsync(); }); ExportCommand = new Command(async _ => { if (DocumentId != Guid.Empty) await ExportDocumentAsync(); else await ExportAsync(); }); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid DocumentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string Search { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string Csv { get; private set; } = string.Empty;
    public ObservableCollection<DocumentAuditRecord> DocumentEntries { get; } = new();
    public ObservableCollection<AuthenticationAuditEntry> Entries { get; } = new();
    public ICommand QueryCommand { get; }
    public ICommand ExportCommand { get; }
    public async Task LoadAsync() { try { var result = await _service.QueryAsync(BuildQuery()); Entries.Clear(); foreach (var entry in result.Entries) Entries.Add(entry); Message = $"{result.TotalCount} audit entries."; Changed(nameof(Message)); } catch (Exception) { Message = "Unable to load the audit log."; Changed(nameof(Message)); } }
    public async Task ExportAsync() { try { Csv = await _service.ExportCsvAsync(BuildQuery()); Message = "Audit log exported."; Changed(nameof(Csv)); Changed(nameof(Message)); } catch (UnauthorizedAccessException) { Message = "Only administrators can export the audit log."; Changed(nameof(Message)); } catch (Exception) { Message = "Unable to export the audit log."; Changed(nameof(Message)); } }
    public async Task LoadDocumentAsync() { try { var result = await _service.QueryDocumentAsync(DocumentId, BuildQuery()); DocumentEntries.Clear(); foreach (var entry in result) DocumentEntries.Add(entry); Message = $"{result.Count} audit entries."; Changed(nameof(Message)); } catch (UnauthorizedAccessException) { Message = "You are not authorized to view this audit trail."; Changed(nameof(Message)); } catch (Exception) { Message = "Unable to load the document audit trail."; Changed(nameof(Message)); } }
    public async Task ExportDocumentAsync() { try { Csv = await _service.ExportDocumentCsvAsync(DocumentId, BuildQuery()); Message = "Audit trail exported."; Changed(nameof(Csv)); Changed(nameof(Message)); } catch (Exception) { Message = "Unable to export the audit trail."; Changed(nameof(Message)); } }
    private AuditLogQuery BuildQuery() { DateTimeOffset? from = Parse(From); DateTimeOffset? to = Parse(To); return new AuditLogQuery(from, to, Null(Action), Null(Username), Null(Actor), DocumentId: DocumentId == Guid.Empty ? null : DocumentId, WorkflowStatus: Null(WorkflowStatus), Revision: Null(Revision), Search: Null(Search)); }
    private static DateTimeOffset? Parse(string value) => string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.TryParse(value, out var result) ? result : throw new FormatException("Enter valid audit date filters.");
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private sealed class Command(Func<object?, Task> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await execute(parameter); }
}
