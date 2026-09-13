using System.Collections.ObjectModel;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class WorkflowViewModel : TaskScreenViewModel
{
    private readonly IWorkflowService? _service;
    private string _note = string.Empty;
    private string _message = string.Empty;
    public WorkflowViewModel() { }
    public WorkflowViewModel(IWorkflowService service) { _service = service ?? throw new ArgumentNullException(nameof(service)); SubmitCommand = new Command(async _ => await SubmitAsync()); CancelCommand = new Command(async _ => await CancelAsync()); }
    public string Note { get => _note; set { _note = value ?? string.Empty; MarkDirtyFromUserInput(); RaisePropertyChanged(nameof(Note)); } }
    public string Message { get => _message; private set { _message = value; RaisePropertyChanged(nameof(Message)); } }
    public ObservableCollection<WorkflowQueueItem> Queue { get; } = new();
    public Guid PackageId { get; set; }
    public bool IsConfirmationVisible { get; private set; }
    public ICommand SubmitCommand { get; } = new Command(_ => { });
    public ICommand CancelCommand { get; } = new Command(_ => { });
    public void BeginSubmission(Guid packageId) { PackageId = packageId; IsConfirmationVisible = true; RaisePropertyChanged(nameof(IsConfirmationVisible)); }
    private async Task SubmitAsync()
    {
        if (_service is null) return;
        try { var result = await _service.SubmitForReviewAsync(new SubmissionRequest(PackageId, Note)); Message = result.Message; IsConfirmationVisible = false; IsDirty = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); await LoadQueueAsync(); }
        catch (UnauthorizedAccessException) { Message = "Only a Maker can submit drawings for review."; }
        catch (Exception) { Message = "The drawing could not be submitted for review."; }
    }
    private async Task CancelAsync() { if (_service is not null) await _service.RecordCancellationAsync(PackageId, 1, Note); IsConfirmationVisible = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); }
    public async Task LoadQueueAsync() { if (_service is null) return; Queue.Clear(); foreach (var item in await _service.GetCheckerQueueAsync()) Queue.Add(item); }
    private sealed class Command(Action<object?> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => execute(parameter); }
}
