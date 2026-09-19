using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class SharedDocumentsViewModel : INotifyPropertyChanged
{
    private readonly ApprovedDocumentService _service;
    private string _search = string.Empty;
    private string _status = string.Empty;
    public ObservableCollection<SharedDocument> Documents { get; } = new();
    public string Search { get => _search; set { _search = value; PropertyChanged?.Invoke(this, new(nameof(Search))); } }
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public ICommand SearchCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public SharedDocumentsViewModel(ApprovedDocumentService service) { _service = service; SearchCommand = new Command(async _ => await LoadAsync()); _ = LoadAsync(); }
    public async Task LoadAsync()
    {
        try { var rows = await _service.ListAsync(new SharedDocumentQuery(Search)); Documents.Clear(); foreach (var row in rows) Documents.Add(row); Status = rows.Count == 0 ? "No approved documents have been shared with you." : string.Empty; }
        catch (Exception exception) { Status = exception.Message; }
    }
    private sealed class Command(Func<object?, Task> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await execute(parameter); }
}
