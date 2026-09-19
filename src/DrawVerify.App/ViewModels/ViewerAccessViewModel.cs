using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class ViewerAccessViewModel : INotifyPropertyChanged
{
    private readonly IViewerAccessService _service;
    private readonly Guid _documentId;
    private readonly Guid _versionId;
    private string _search = string.Empty;
    private string _status = string.Empty;
    private string _error = string.Empty;
    private ViewerAccessTarget? _selectedTarget;
    private DateTime? _expirationUtc;
    public ObservableCollection<ViewerAccessTarget> Targets { get; } = new();
    public ObservableCollection<ViewerAccessGrant> Grants { get; } = new();
    public string Search { get => _search; set { _search = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string ErrorMessage { get => _error; private set { _error = value; Changed(); } }
    public ViewerAccessTarget? SelectedTarget { get => _selectedTarget; set { _selectedTarget = value; Changed(); ((Command)GrantCommand).Raise(); ((Command)RevokeCommand).Raise(); } }
    public DateTime? ExpirationUtc { get => _expirationUtc; set { _expirationUtc = value; Changed(); } }
    public bool CanManageAccess => _service.CanManageAccess;
    public bool CanSetExpiration { get; }
    public bool CanUseBulkActions { get; }
    public bool IsAvailable => CanManageAccess && string.IsNullOrEmpty(ErrorMessage);
    public ICommand SearchCommand { get; }
    public ICommand GrantCommand { get; }
    public ICommand RevokeCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ViewerAccessViewModel(IViewerAccessService service, Guid documentId, Guid versionId, ViewerAccessConfiguration? configuration = null)
    {
        _service = service; _documentId = documentId; _versionId = versionId; CanSetExpiration = configuration?.AllowExpiration == true; CanUseBulkActions = configuration?.AllowBulkActions == true;
        SearchCommand = new Command(async _ => await SearchAsync()); GrantCommand = new Command(async _ => await GrantAsync(), _ => SelectedTarget is not null && CanManageAccess); RevokeCommand = new Command(async _ => await RevokeAsync(), _ => SelectedTarget is not null && CanManageAccess);
        _ = LoadAsync();
    }
    public async Task LoadAsync()
    {
        if (!CanManageAccess) { ErrorMessage = "Viewer access controls are available only to Approvers."; return; }
        try { var rows = await _service.ListGrantsAsync(_documentId, _versionId); Grants.Clear(); foreach (var row in rows) Grants.Add(row); await SearchAsync(); }
        catch (Exception exception) { ErrorMessage = exception.Message; Status = "Unable to load Viewer access."; }
    }
    private async Task SearchAsync()
    {
        try { var rows = await _service.SearchTargetsAsync(Search); Targets.Clear(); foreach (var row in rows) Targets.Add(row); ErrorMessage = string.Empty; }
        catch (Exception exception) { ErrorMessage = exception.Message; Status = "Unable to search Viewer targets."; }
    }
    private async Task GrantAsync()
    {
        if (SelectedTarget is null) return; await MutateAsync(() => _service.GrantAsync(_documentId, _versionId, new ViewerAccessChange(new[] { SelectedTarget }, ExpirationUtc)));
    }
    private async Task RevokeAsync()
    {
        if (SelectedTarget is null) return; await MutateAsync(() => _service.RevokeAsync(_documentId, _versionId, new[] { SelectedTarget }));
    }
    private async Task MutateAsync(Func<Task<ViewerAccessMutationResult>> operation)
    {
        try { var result = await operation(); if (!result.Succeeded) { ErrorMessage = string.Join("; ", result.Errors); return; } Status = result.Summary; ErrorMessage = string.Empty; await LoadAsync(); }
        catch (Exception exception) { ErrorMessage = exception.Message; Status = "Viewer access was not updated."; }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private sealed class Command(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public async void Execute(object? parameter) => await execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
