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
    private WorkflowStatus? _currentStatus;
    private SubmissionRequest? _submissionRequest;
    private readonly ICentralizedLogger? _logger;
    private ReviewPackage? _reviewPackage;
    private string _reviewComment = string.Empty;
    private string _reviewError = string.Empty;
    private readonly HashSet<string> _viewedSections = new(StringComparer.OrdinalIgnoreCase);
    private bool _decisionConfirmed;
    private ReviewDecisionType? _pendingDecision;
    private ReviewFinding? _selectedFinding;
    public WorkflowViewModel() { SubmitCommand = new Command(async _ => await SubmitAsync(), _ => false); CancelCommand = new Command(async _ => await CancelAsync(true), _ => IsConfirmationVisible); InitializeReviewCommands(); }
    public WorkflowViewModel(IWorkflowService service, ISessionAuthenticationService? authentication = null, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service)); _logger = logger;
        _canSubmit = authorization?.IsAllowed(authentication?.CurrentUser, RolePermission.SubmitForReview) ?? authentication?.CurrentUser?.HasRole(SystemRoles.Maker) == true;
        SubmitCommand = new Command(async _ => await SubmitAsync(), _ => _canSubmit && IsConfirmationVisible); CancelCommand = new Command(async _ => await CancelAsync(true), _ => IsConfirmationVisible);
        InitializeReviewCommands();
    }
    public string Note { get => _note; set { _note = value ?? string.Empty; MarkDirtyFromUserInput(); RaisePropertyChanged(nameof(Note)); } }
    public string Message { get => _message; private set { _message = value; RaisePropertyChanged(nameof(Message)); } }
    public WorkflowStatus? CurrentStatus { get => _currentStatus; private set { _currentStatus = value; RaisePropertyChanged(nameof(CurrentStatus)); } }
    public ObservableCollection<WorkflowQueueItem> Queue { get; } = new();
    public ObservableCollection<ReviewPackage> MakerPackages { get; } = new();
    public ObservableCollection<ReviewComment> MakerReviewComments { get; } = new();
    public ReviewPackage? ReviewPackage { get => _reviewPackage; private set { _reviewPackage = value; RaisePropertyChanged(nameof(ReviewPackage)); RaiseReviewCommands(); } }
    public string ReviewComment { get => _reviewComment; set { _reviewComment = value ?? string.Empty; RaisePropertyChanged(nameof(ReviewComment)); RaiseReviewCommands(); } }
    public string ReviewError { get => _reviewError; private set { _reviewError = value; RaisePropertyChanged(nameof(ReviewError)); } }
    public bool IsReviewMode => _reviewPackage is not null;
    public bool RequireSectionConfirmation { get; set; }
    public bool HasViewedKeySections => _viewedSections.Contains("Verification") && _viewedSections.Contains("Documents");
    public ReviewFinding? SelectedVerificationFinding { get => _selectedFinding; private set { _selectedFinding = value; RaisePropertyChanged(nameof(SelectedVerificationFinding)); } }
    public Guid PackageId { get; set; }
    public bool IsConfirmationVisible { get; private set; }
    public event Action<string>? NavigationRequested;
    public event Action<SubmissionRequest>? ReturnRequested;
    public event Action? DecisionConfirmationRequested;
    public event Action<ReviewFinding>? VerificationNavigationRequested;
    public event Action<ReviewPackage>? MakerPackageSelected;
    public ICommand SubmitCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApproveCommand { get; private set; } = null!;
    public ICommand RejectCommand { get; private set; } = null!;
    public ICommand RequestChangesCommand { get; private set; } = null!;
    public ICommand SaveReviewCommentCommand { get; private set; } = null!;
    public ICommand MarkDocumentsViewedCommand { get; private set; } = null!;
    public ICommand MarkVerificationViewedCommand { get; private set; } = null!;
    private void InitializeReviewCommands()
    {
        ApproveCommand = new Command(async _ => await DecideAsync(ReviewDecisionType.Approve), _ => ReviewPackage is not null);
        RejectCommand = new Command(async _ => await DecideAsync(ReviewDecisionType.Reject), _ => ReviewPackage is not null && !string.IsNullOrWhiteSpace(ReviewComment));
        RequestChangesCommand = new Command(async _ => await DecideAsync(ReviewDecisionType.RequestChanges), _ => ReviewPackage is not null && !string.IsNullOrWhiteSpace(ReviewComment));
        SaveReviewCommentCommand = new Command(async _ => await SaveReviewCommentAsync(), _ => ReviewPackage is not null && !string.IsNullOrWhiteSpace(ReviewComment));
        MarkDocumentsViewedCommand = new Command(_ => MarkSectionViewed("Documents"), _ => ReviewPackage is not null);
        MarkVerificationViewedCommand = new Command(_ => MarkSectionViewed("Verification"), _ => ReviewPackage is not null);
    }
    private void RaiseReviewCommands() { ((Command)ApproveCommand).Raise(); ((Command)RejectCommand).Raise(); ((Command)RequestChangesCommand).Raise(); ((Command)SaveReviewCommentCommand).Raise(); }
    public async Task OpenReviewAsync(WorkflowQueueItem item)
    {
        try { ReviewPackage = await _service!.OpenReviewPackageAsync(item.PackageId, item.Revision); RaisePropertyChanged(nameof(IsReviewMode)); ReviewError = ReviewPackage is null ? "The review package was not found or is no longer available." : string.Empty; }
        catch (Exception exception) { _logger?.Log(exception, "WPF review open", $"Unable to open packageId={item.PackageId}, revision={item.Revision}."); ReviewError = exception is UnauthorizedAccessException ? "You are not authorized to review this package." : "Unable to open the review package."; }
    }
    private async Task DecideAsync(ReviewDecisionType type)
    {
        if (ReviewPackage is null || _service is null) return;
        if (RequireSectionConfirmation && !HasViewedKeySections && !_decisionConfirmed) { _pendingDecision = type; DecisionConfirmationRequested?.Invoke(); ReviewError = "Review the Documents and Verification sections, then confirm the decision."; return; }
        try { await _service.DecideAsync(ReviewPackage.PackageId, ReviewPackage.Revision, type, ReviewComment); ReviewError = string.Empty; await LoadQueueAsync(); }
        catch (Exception exception) { _logger?.Log(exception, "WPF review decision", $"Unable to complete review decision notification for packageId={ReviewPackage.PackageId}, revision={ReviewPackage.Revision}."); ReviewError = exception is ArgumentException ? exception.Message : "The decision was saved, but Maker notification delivery failed."; }
    }
    public void MarkSectionViewed(string section) { if (!string.IsNullOrWhiteSpace(section)) _viewedSections.Add(section); RaisePropertyChanged(nameof(HasViewedKeySections)); }
    public void ConfirmDecision() { _decisionConfirmed = true; var pending = _pendingDecision; _pendingDecision = null; if (pending is not null) _ = DecideAsync(pending.Value); }
    public void SelectVerificationFinding(ReviewFinding finding) { SelectedVerificationFinding = finding ?? throw new ArgumentNullException(nameof(finding)); VerificationNavigationRequested?.Invoke(finding); }
    public async Task LoadMakerReviewPackagesAsync()
    {
        if (_service is null) return;
        try { MakerPackages.Clear(); MakerReviewComments.Clear(); foreach (var package in await _service.GetMakerReviewPackagesAsync()) { MakerPackages.Add(package); foreach (var comment in package.Comments) MakerReviewComments.Add(comment); } }
        catch (UnauthorizedAccessException) { }
        catch (Exception exception) { _logger?.Log(exception, "WPF Maker review comments", "Unable to load Maker-visible review packages."); }
    }
    public async Task SelectMakerPackageAsync(ReviewPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        try { MakerReviewComments.Clear(); foreach (var comment in (await _service!.GetReviewPackageForReaderAsync(package.PackageId, package.Revision))?.Comments ?? Array.Empty<ReviewComment>()) MakerReviewComments.Add(comment); MakerPackageSelected?.Invoke(package); }
        catch (Exception exception) { _logger?.Log(exception, "WPF Maker review comments", $"Unable to load comments for packageId={package.PackageId}, revision={package.Revision}."); ReviewError = exception is UnauthorizedAccessException ? "You are not authorized to view these review comments." : "Unable to load review comments."; }
    }
    private async Task SaveReviewCommentAsync()
    {
        if (ReviewPackage is null || _service is null) return;
        try { await _service.AddReviewCommentAsync(ReviewPackage.PackageId, ReviewPackage.Revision, ReviewComment); ReviewComment = string.Empty; ReviewError = string.Empty; }
        catch (Exception exception) { _logger?.Log(exception, "WPF review comment", $"Unable to save review comment for packageId={ReviewPackage.PackageId}, revision={ReviewPackage.Revision}."); ReviewError = exception is ArgumentException ? exception.Message : "The review comment could not be saved."; }
    }
    public async void BeginSubmission(Guid packageId)
    {
        PackageId = packageId; IsConfirmationVisible = true; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise();
        if (_service is not null) { var state = await _service.GetPackageAsync(packageId); CurrentStatus = state?.Status; _canSubmit &= state is not null && state.Status is (WorkflowStatus.Draft or WorkflowStatus.InPreparation); if (!_canSubmit && state is not null) Message = $"This package is currently '{state.Status}' and cannot be submitted for review."; ((Command)SubmitCommand).Raise(); }
    }
    public void BeginSubmission(SubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _submissionRequest = request;
        Note = request.Note ?? string.Empty;
        BeginSubmission(request.PackageId);
    }
    private async Task SubmitAsync()
    {
        if (_service is null) return;
        try { var request = (_submissionRequest ?? new SubmissionRequest(PackageId)) with { Note = Note }; var result = await _service.SubmitForReviewAsync(request); if (!result.Succeeded) { Message = result.Message; return; } Message = result.Message; IsConfirmationVisible = false; IsDirty = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise(); await LoadQueueAsync(); NavigationRequested?.Invoke("Submitted"); }
        catch (UnauthorizedAccessException) { Message = "Only a Maker can submit drawings for review."; }
        catch (Exception exception) { _logger?.Log(exception, "WPF workflow submission", $"Unable to submit packageId={PackageId}."); Message = "The drawing could not be submitted for review."; }
    }
    public Task CancelAndReturnAsync() => CancelAsync(true);
    private async Task CancelAsync(bool returnToPrevious = false) { try { var state = _service is null ? null : await _service.GetPackageAsync(PackageId); if (_service is not null) await _service.RecordCancellationAsync(PackageId, state?.Revision ?? 0, Note); IsConfirmationVisible = false; RaisePropertyChanged(nameof(IsConfirmationVisible)); ((Command)SubmitCommand).Raise(); ((Command)CancelCommand).Raise(); if (returnToPrevious) ReturnRequested?.Invoke((_submissionRequest ?? new SubmissionRequest(PackageId)) with { Note = Note }); } catch (Exception exception) { _logger?.Log(exception, "WPF workflow cancellation", $"Unable to record cancellation for packageId={PackageId}."); Message = "Unable to cancel submission."; } }
    public async Task LoadQueueAsync() { if (_service is null) return; try { Queue.Clear(); foreach (var item in await _service.GetCheckerQueueAsync()) Queue.Add(item); } catch (UnauthorizedAccessException) { ReviewError = "Only Checkers and Approvers can access the review queue."; } catch (Exception exception) { _logger?.Log(exception, "WPF review queue", "Unable to load the Checker review queue."); ReviewError = "Unable to load the review queue."; } }
    private sealed class Command(Action<object?> execute, Predicate<object?> canExecute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(parameter); public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
