using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class UserAdministrationViewModel : INotifyPropertyChanged
{
    private readonly UserAdministrationService _service;
    private string _username = string.Empty, _displayName = string.Empty, _email = string.Empty, _password = string.Empty, _resetPassword = string.Empty, _selectedUsername = string.Empty;
    private UserAccount? _selectedUser;
    private bool _canEditRoles;
    private string _usernameError = string.Empty, _displayNameError = string.Empty, _initialPasswordError = string.Empty;
    private string _creationSnapshot = string.Empty;
    private long _roleVersion;
    public UserAdministrationViewModel(UserAdministrationService service)
    {
        _service = service; RoleCatalog = new(SystemRoles.Catalog); _canEditRoles = service.CanEditRoles;
        RoleOptions = new(RoleCatalog.Select(role => new RoleSelectionOption(role, false, ToggleRole)));
        AssignedRoles = new(service.CurrentUserRoles);
        CreateCommand = new Command(async _ => await CreateAsync(), _ => !IsBusy && CanEditRoles);
        SetRolesCommand = new Command(async _ => await SetRolesAsync(), _ => !IsBusy && CanEditRoles && SelectedUser is not null);
        UpdateProfileCommand = new Command(async _ => await UpdateProfileAsync(), _ => !IsBusy && CanEditRoles && SelectedUser is not null);
        ResetPasswordCommand = new Command(async _ => await ResetPasswordAsync(), _ => !IsBusy && CanEditRoles && SelectedUser is not null);
        DeactivateCommand = new Command(async _ => await DeactivateAsync(), _ => !IsBusy && CanEditRoles && SelectedUser is not null);
        ToggleRoleCommand = new Command(role => ToggleRole(role as string), _ => CanEditRoles);
        CancelCommand = new Command(_ => CancelRequested?.Invoke(), _ => !IsBusy && CanEditRoles);
        BackCommand = new Command(_ => BackRequested?.Invoke(), _ => !IsBusy && CanEditRoles);
        RoleCancelCommand = new Command(_ => RoleCancelRequested?.Invoke(), _ => !IsBusy && CanEditRoles);
        RoleBackCommand = new Command(_ => RoleBackRequested?.Invoke(), _ => !IsBusy && CanEditRoles);
        CreateAnotherCommand = new Command(_ => ResetCreationForm(), _ => !IsBusy && CanEditRoles);
        _creationSnapshot = CreationState;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? AuthorizationStateChanged;
    public event Action? CancelRequested;
    public event Action? BackRequested;
    public event Action? RoleCancelRequested;
    public event Action? RoleBackRequested;
    public event Action? CreationFormReset;
    public string Username { get => _username; set { _username = value; Changed(); Changed(nameof(IsCreationDirty)); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); Changed(nameof(IsCreationDirty)); } }
    public string Email { get => _email; set { _email = value; Changed(); } }
    public string InitialPassword { get => _password; set { _password = value; Changed(); Changed(nameof(IsCreationDirty)); } }
    public string UsernameError { get => _usernameError; private set { _usernameError = value; Changed(); } }
    public string DisplayNameError { get => _displayNameError; private set { _displayNameError = value; Changed(); } }
    public string InitialPasswordError { get => _initialPasswordError; private set { _initialPasswordError = value; Changed(); } }
    public bool IsCreationDirty => CreationState != _creationSnapshot;
    public string ResetPassword { get => _resetPassword; set { _resetPassword = value; Changed(); } }
    public string SelectedUsername { get => _selectedUsername; set { _selectedUsername = value; SelectUser(Users.FirstOrDefault(x => string.Equals(x.Username, value, StringComparison.OrdinalIgnoreCase))); Changed(); } }
    public UserAccount? SelectedUser { get => _selectedUser; set { _selectedUser = value; _selectedUsername = value?.Username ?? string.Empty; if (value is not null) { DisplayName = value.DisplayName; Email = value.Email ?? string.Empty; } CurrentRoles.Clear(); foreach (var role in value?.Roles ?? Array.Empty<string>()) if (SystemRoles.TryCanonicalize(role, out var canonical)) CurrentRoles.Add(canonical); _roleVersion = value?.RoleVersion ?? 0; AssignedRoles.Clear(); foreach (var role in CurrentRoles) AssignedRoles.Add(role); foreach (var option in RoleOptions) option.SetSelected(AssignedRoles.Contains(option.Role.Id, StringComparer.OrdinalIgnoreCase)); Changed(); Changed(nameof(SelectedUsername)); Changed(nameof(IsRoleDirty)); ((Command)SetRolesCommand).Raise(); ((Command)UpdateProfileCommand).Raise(); ((Command)ResetPasswordCommand).Raise(); ((Command)DeactivateCommand).Raise(); } }
    public string Message { get; private set; } = string.Empty;
    public bool IsBusy { get; private set; }
    public bool CanEditRoles => _canEditRoles;
    public ObservableCollection<RoleDefinition> RoleCatalog { get; }
    public ObservableCollection<RoleSelectionOption> RoleOptions { get; }
    public ObservableCollection<string> AssignedRoles { get; }
    public ObservableCollection<string> CurrentRoles { get; } = new();
    public bool IsRoleDirty => !new HashSet<string>(CurrentRoles, StringComparer.OrdinalIgnoreCase).SetEquals(AssignedRoles);
    public Func<string, bool>? RoleConfirmation { get; set; }
    public ObservableCollection<UserAccount> Users { get; } = new();
    public ICommand CreateCommand { get; }
    public ICommand SetRolesCommand { get; }
    public ICommand UpdateProfileCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ToggleRoleCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RoleCancelCommand { get; }
    public ICommand RoleBackCommand { get; }
    public ICommand CreateAnotherCommand { get; }

    private string CreationState => string.Join("\u001f", Username, DisplayName, InitialPassword);
    public void DiscardCreation() => ResetCreationForm();

    public void ResetCreationForm()
    {
        Username = string.Empty; DisplayName = string.Empty; InitialPassword = string.Empty;
        ClearCreationErrors(); Message = string.Empty; Changed(nameof(Message));
        _creationSnapshot = CreationState; Changed(nameof(IsCreationDirty));
        CreationFormReset?.Invoke();
    }

    public async Task LoadUsersAsync()
    {
        if (!CanEditRoles) return;
        try
        {
            var users = await _service.ListUsersAsync(); Users.Clear(); foreach (var user in users) Users.Add(user);
            if (SelectedUser is not null && !IsRoleDirty) SelectUser(Users.FirstOrDefault(x => x.Id == SelectedUser.Id));
        }
        catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); }
    }
    public async Task UpdateProfileAsync()
    {
        if (SelectedUser is null) { Message = "Select a user before editing the profile."; Changed(nameof(Message)); return; }
        await RunOperationAsync(() => _service.UpdateProfileAsync(new UserProfileUpdateRequest(SelectedUser.Username, DisplayName, Email)), "Unable to update the user profile.");
    }
    public async Task ResetPasswordAsync()
    {
        if (SelectedUser is null) { Message = "Select a user before resetting the password."; Changed(nameof(Message)); return; }
        await RunOperationAsync(() => _service.UpdateProfileAsync(new UserProfileUpdateRequest(SelectedUser.Username, PasswordReset: true, NewPassword: ResetPassword)), "Unable to reset the user password.");
        if (string.IsNullOrEmpty(Message) || Message == "User profile updated.") ResetPassword = string.Empty;
    }
    public async Task DeactivateAsync()
    {
        if (SelectedUser is null) { Message = "Select a user before deactivating the account."; Changed(nameof(Message)); return; }
        await RunOperationAsync(() => _service.DeactivateUserAsync(new UserDeactivationRequest(SelectedUser.Username)), "Unable to deactivate the user account.");
    }
    private async Task RunOperationAsync(Func<Task<AuthenticationResult>> operation, string failureMessage)
    {
        if (IsBusy) return; IsBusy = true; Changed(nameof(IsBusy));
        try { var result = await operation(); Message = result.Message; Changed(nameof(Message)); if (result.Succeeded) await LoadUsersAsync(); }
        catch (Exception) { Message = failureMessage; Changed(nameof(Message)); }
        finally { IsBusy = false; Changed(nameof(IsBusy)); ((Command)UpdateProfileCommand).Raise(); ((Command)ResetPasswordCommand).Raise(); ((Command)DeactivateCommand).Raise(); }
    }
    public async Task CreateAsync()
    {
        if (IsBusy) return; IsBusy = true; Changed(nameof(IsBusy));
        ClearCreationErrors();
        var submittedUsername = Username;
        try
        {
            var result = await _service.CreateUserAsync(new(Username, DisplayName, InitialPassword));
            if (result.Succeeded)
            {
                await LoadUsersAsync(); ResetCreationForm();
                Message = $"User '{UserAdministrationService.Normalize(submittedUsername)}' created successfully";
                Changed(nameof(Message));
            }
            else
            {
                Message = result.Message; Changed(nameof(Message)); MapCreationError(result);
            }
        }
        catch (Exception) { Message = "Unable to create the user."; Changed(nameof(Message)); }
        finally { IsBusy = false; Changed(nameof(IsBusy)); ((Command)CreateCommand).Raise(); ((Command)CancelCommand).Raise(); ((Command)CreateAnotherCommand).Raise(); }
    }

    private void MapCreationError(AuthenticationResult result)
    {
        if (result.Kind == AuthenticationResultKind.DuplicateUsername || result.Message.Contains("username", StringComparison.OrdinalIgnoreCase)) UsernameError = result.Message;
        else if (result.Message.Contains("display name", StringComparison.OrdinalIgnoreCase)) DisplayNameError = result.Message;
        else if (result.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("character", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("uppercase", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("lowercase", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("digit", StringComparison.OrdinalIgnoreCase)) InitialPasswordError = result.Message;
    }

    private void ClearCreationErrors() { UsernameError = string.Empty; DisplayNameError = string.Empty; InitialPasswordError = string.Empty; }
    public async Task SetRolesAsync()
    {
        if (!CanEditRoles || SelectedUser is null) { Message = "Only an administrator can change user roles."; Changed(nameof(Message)); return; }
        SyncAssignedRolesFromOptions();
        if (RoleConfirmation is null && (AssignedRoles.Count == 0 || IsAdminRemoval())) { Message = "Role change confirmation is unavailable; no changes were saved."; Changed(nameof(Message)); return; }
        if (AssignedRoles.Count == 0 && !RoleConfirmation!("This user will have no roles. Do you want to continue?")) return;
        if (AssignedRoles.Count > 0 && IsAdminRemoval() && !RoleConfirmation!("This will remove the last Admin role from this user. Do you want to continue?")) return;
        IsBusy = true; Changed(nameof(IsBusy));
        try
        {
            var isSelfUpdate = string.Equals(SelectedUser.Username, _service.CurrentUsername, StringComparison.OrdinalIgnoreCase);
            var result = await _service.SetUserRolesAsync(SelectedUser.Username, AssignedRoles, _roleVersion);
            if (result.Succeeded)
            {
                var username = SelectedUser.Username;
                await LoadUsersAsync();
                SelectUser(Users.FirstOrDefault(x => x.Id == SelectedUser.Id));
                Message = $"Roles updated for '{username}'.";
                Changed(nameof(Message));
                if (isSelfUpdate) RefreshAuthorizationState();
            }
            else
            {
                var errorMessage = result.Message;
                await RefreshUsersPreservingRoleDraftAsync();
                Message = errorMessage;
                Changed(nameof(Message));
            }
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            await RefreshUsersPreservingRoleDraftAsync();
            Message = errorMessage;
            Changed(nameof(Message));
        }
        finally { IsBusy = false; Changed(nameof(IsBusy)); ((Command)SetRolesCommand).Raise(); ((Command)RoleCancelCommand).Raise(); ((Command)RoleBackCommand).Raise(); }
    }
    public void DiscardRoles() { AssignedRoles.Clear(); foreach (var role in CurrentRoles) AssignedRoles.Add(role); foreach (var option in RoleOptions) option.SetSelected(AssignedRoles.Contains(option.Role.Id, StringComparer.OrdinalIgnoreCase)); RefreshRoleOptionState(); Changed(nameof(IsRoleDirty)); }
    private void SyncAssignedRolesFromOptions()
    {
        AssignedRoles.Clear();
        foreach (var option in RoleOptions.Where(x => x.IsSelected)) AssignedRoles.Add(option.Role.Id);
        Changed(nameof(IsRoleDirty));
        RefreshRoleOptionState();
    }
    private void RefreshRoleOptionState()
    {
        foreach (var option in RoleOptions) option.RefreshState(CurrentRoles.Contains(option.Role.Id, StringComparer.OrdinalIgnoreCase));
    }
    private bool IsAdminRemoval() => CurrentRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase) && !AssignedRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
    private async Task RefreshUsersPreservingRoleDraftAsync()
    {
        var selectedId = SelectedUser?.Id;
        var users = await _service.ListUsersAsync(); Users.Clear(); foreach (var user in users) Users.Add(user);
        var refreshed = selectedId is null ? null : Users.FirstOrDefault(x => x.Id == selectedId.Value);
        if (refreshed is not null) { _selectedUser = refreshed; _roleVersion = refreshed.RoleVersion; CurrentRoles.Clear(); foreach (var role in refreshed.Roles) if (SystemRoles.TryCanonicalize(role, out var canonical)) CurrentRoles.Add(canonical); RefreshRoleOptionState(); Changed(nameof(CurrentRoles)); Changed(nameof(IsRoleDirty)); }
    }
    private void RefreshAuthorizationState()
    {
        var canEditRoles = _service.CanEditRoles;
        _canEditRoles = canEditRoles;
        Changed(nameof(CanEditRoles));
        ((Command)CreateCommand).Raise();
        ((Command)SetRolesCommand).Raise();
        ((Command)ToggleRoleCommand).Raise();
        ((Command)UpdateProfileCommand).Raise(); ((Command)ResetPasswordCommand).Raise(); ((Command)DeactivateCommand).Raise();
        AuthorizationStateChanged?.Invoke();
    }
    public void ToggleRole(string? role)
    {
        if (!CanEditRoles || !SystemRoles.TryCanonicalize(role, out var canonical)) return;
        var option = RoleOptions.FirstOrDefault(x => x.Role.Id == canonical);
        if (option is null) return;
        var isCurrentlyProposed = AssignedRoles.Contains(canonical, StringComparer.OrdinalIgnoreCase);
        if (isCurrentlyProposed) AssignedRoles.Remove(canonical);
        else AssignedRoles.Add(canonical);
        option.SetSelected(!isCurrentlyProposed);
        RefreshRoleOptionState();
        Changed(nameof(IsRoleDirty));
    }
    private void SelectUser(UserAccount? user)
    {
        SelectedUser = user;
    }
    private void ToggleRole(RoleSelectionOption option) { if (!CanEditRoles) return; if (option.IsSelected && !AssignedRoles.Contains(option.Role.Id, StringComparer.OrdinalIgnoreCase)) AssignedRoles.Add(option.Role.Id); else if (!option.IsSelected) AssignedRoles.Remove(option.Role.Id); Changed(nameof(IsRoleDirty)); option.RefreshState(CurrentRoles.Contains(option.Role.Id, StringComparer.OrdinalIgnoreCase)); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private sealed class Command(Action<object?> execute, Predicate<object?>? can = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => can?.Invoke(p) ?? true; public void Execute(object? p) => execute(p); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}

public sealed class RoleSelectionOption : INotifyPropertyChanged
{
    private readonly Action<RoleSelectionOption> _changed;
    private bool _isSelected;
    public RoleSelectionOption(RoleDefinition role, bool isSelected, Action<RoleSelectionOption> changed) { Role = role; _isSelected = isSelected; _changed = changed; }
    public RoleDefinition Role { get; }
    public string Id => Role.Id;
    public string Name => Role.Name;
    public string Description => Role.Description;
    public bool IsCurrent { get; private set; }
    public bool IsPending { get; private set; }
    public string StateLabel => IsPending ? (IsCurrent ? "Current role — pending removal" : "Added — pending save") : "Current role";
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); _changed(this); } }
    internal void SetSelected(bool value) { var changed = _isSelected != value; _isSelected = value; IsCurrent = value; IsPending = false; if (changed) PropertyChanged?.Invoke(this, new(nameof(IsSelected))); PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); PropertyChanged?.Invoke(this, new(nameof(IsPending))); PropertyChanged?.Invoke(this, new(nameof(StateLabel))); }
    internal void RefreshState(bool wasCurrent) { IsCurrent = wasCurrent; IsPending = wasCurrent != IsSelected; PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); PropertyChanged?.Invoke(this, new(nameof(IsPending))); PropertyChanged?.Invoke(this, new(nameof(StateLabel))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}
