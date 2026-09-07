using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Models;
namespace DrawVerify.App.ViewModels;
public sealed class AuditLogViewModel : INotifyPropertyChanged
{
    private readonly AuditLogService _service; public AuditLogViewModel(AuditLogService service) { _service = service; QueryCommand = new Command(async _ => await LoadAsync()); ExportCommand = new Command(async _ => await ExportAsync()); }
    public event PropertyChangedEventHandler? PropertyChanged; public string Action { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string Actor { get; set; } = string.Empty; public string Message { get; private set; } = string.Empty; public string Csv { get; private set; } = string.Empty; public ObservableCollection<AuthenticationAuditEntry> Entries { get; } = new(); public ICommand QueryCommand { get; } public ICommand ExportCommand { get; }
    public async Task LoadAsync() { try { var result = await _service.QueryAsync(new AuditLogQuery(Action: Null(Action), Username: Null(Username), ActorUsername: Null(Actor))); Entries.Clear(); foreach (var entry in result.Entries) Entries.Add(entry); Message = $"{result.TotalCount} audit entries."; Changed(nameof(Message)); } catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); } }
    public async Task ExportAsync() { try { Csv = await _service.ExportCsvAsync(new AuditLogQuery(Action: Null(Action), Username: Null(Username), ActorUsername: Null(Actor))); Message = "Audit log exported."; Changed(nameof(Csv)); Changed(nameof(Message)); } catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); } }
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim(); private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); private sealed class Command(Func<object?, Task> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await execute(parameter); }
}
