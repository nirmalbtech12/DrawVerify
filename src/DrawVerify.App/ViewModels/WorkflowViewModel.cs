using System.Collections.ObjectModel;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;

namespace DrawVerify.App.ViewModels;

public sealed class WorkflowViewModel : TaskScreenViewModel
{
    private readonly IWorkflowService? _service;
    private string _note = string.Empty;
    private string _message = string.Empty;
    private bool _canSubmit;
    private readonly ICentralizedLogger? _logger;
    public WorkflowViewModel() { SubmitCommand = new Command(async _ => await SubmitAsync(), _ => false); CancelCommand = new Command(async _ => await CancelAsync(), _ => IsConfirmationVisible); }
    public WorkflowViewModel(IWorkflowService service, ISessionAuthenticationService? authentication = null, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service)); _logger = logger;
        _canSubmit = authorization?.IsAllowed(authentication?.CurrentUser, RolePermission.SubmitForReview) ?? authentication?.CurrentUser?.HasRole(SystemRoles.Maker) == true;
        SubmitCommand = new Command(async _ => await SubmitAsync(), _ => _canSubmit && IsConfirmationVisible); CancelCommand = new Command(async _ => await CancelAsync(), _ => IsConfirmationVisible);
    }
    public string Note { get => _note; set { _note = value ?? string.Empty; MarkDirtyFromUserInput(); RaisePropertyChanged(nameof(Note)); } }
    public string Message { get => _message; private set { _message = value; RaisePropertyChanged(nameof(Message)); } }
    public ObservableCollection<WorkflowQueueItem> Queue { get; } = new();
    public Guid PackageId { get; set; }
    public bool IsConfirmationVisible { get; private set; }
    public event Action<string>? NavigationRequested;
    public ICommand SubmitCommand { get; }
    public ICommand CancelCommand { get; }
    public async void BeginSubmission(Guid packageId)
    {
        PackageId = packageId; IsConfirmationVisible = true; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise();
        if (_service is not null) { var state = await _service.GetPackageAsync(packageId); _canSubmit &= state is null || state.Status is WorkflowStatus.Draft or WorkflowStatus.InPreparation; ((Command)SubmitCommand).Raise(); }
    }
    private async Task SubmitAsync()
    {
        if (_service is null) return;
        try { var result = await _service.SubmitForReviewAsync(new SubmissionRequest(PackageId, Note)); if (!result.Succeeded) { Message = result.Message; return; } Message = result.Message; IsConfirmationVisible = false; IsDirty = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise(); await LoadQueueAsync(); NavigationRequested?.Invoke("Submitted"); }
        catch (UnauthorizedAccessException) { Message = "Only a Maker can submit drawings for review."; }
        catch (Exception exception) { _logger?.Log(exception, "WPF workflow submission", $"Unable to submit packageId={PackageId}."); Message = "The drawing could not be submitted for review."; }
    }
    private async Task CancelAsync() { try { if (_service is not null) await _service.RecordCancellationAsync(PackageId, 1, Note); IsConfirmationVisible = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise(); } catch (Exception exception) { _logger?.Log(exception, "WPF workflow cancellation", $"Unable to record cancellation for packageId={PackageId}, revision=1."); Message = "Unable to cancel submission."; } }
    public async Task LoadQueueAsync() { if (_service is null) return; Queue.Clear(); foreach (var item in await _service.GetCheckerQueueAsync()) Queue.Add(item); }
    private sealed class Command(Action<object?> execute, Predicate<object?> canExecute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(parameter); public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
