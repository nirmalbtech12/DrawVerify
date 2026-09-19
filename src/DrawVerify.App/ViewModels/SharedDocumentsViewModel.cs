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
    private SharedDocument? _selectedDocument;
    public ObservableCollection<SharedDocument> Documents { get; } = new();
    public string Search { get => _search; set { _search = value; PropertyChanged?.Invoke(this, new(nameof(Search))); } }
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public SharedDocument? SelectedDocument { get => _selectedDocument; set { _selectedDocument = value; PropertyChanged?.Invoke(this, new(nameof(SelectedDocument))); } }
    public ICommand SearchCommand { get; }
    public ICommand OpenCommand { get; }
    public event Action<SharedDocument>? OpenRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
    public SharedDocumentsViewModel(ApprovedDocumentService service) { _service = service; SearchCommand = new Command(async _ => await LoadAsync()); OpenCommand = new Command(async parameter => { if (parameter is SharedDocument document) { SelectedDocument = document; OpenRequested?.Invoke(document); } await Task.CompletedTask; }); _ = LoadAsync(); }
    public async Task LoadAsync()
    {
        try { var rows = await _service.ListAsync(new SharedDocumentQuery(Search)); Documents.Clear(); foreach (var row in rows) Documents.Add(row); Status = rows.Count == 0 ? "No approved documents have been shared with you." : string.Empty; }
        catch (Exception exception) { Status = exception.Message; }
    }
    public void ClearSelectionAndReload() { SelectedDocument = null; _ = LoadAsync(); }
    private sealed class Command(Func<object?, Task> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await execute(parameter); }
}
