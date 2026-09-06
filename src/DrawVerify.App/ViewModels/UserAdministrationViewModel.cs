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
    private string _username = string.Empty, _displayName = string.Empty, _password = string.Empty, _selectedUsername = string.Empty;
    public UserAdministrationViewModel(UserAdministrationService service)
    {
        _service = service; RoleCatalog = new(SystemRoles.Catalog); AssignedRoles = new(service.CurrentUserRoles); CanEditRoles = service.CanEditRoles;
        CreateCommand = new Command(async _ => await CreateAsync(), _ => !IsBusy && CanEditRoles);
        SetRolesCommand = new Command(async _ => await SetRolesAsync(), _ => !IsBusy && CanEditRoles && !string.IsNullOrWhiteSpace(SelectedUsername));
        ToggleRoleCommand = new Command(role => ToggleRole(role as string), _ => CanEditRoles);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Username { get => _username; set { _username = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public string InitialPassword { get => _password; set { _password = value; Changed(); } }
    public string SelectedUsername { get => _selectedUsername; set { _selectedUsername = value; var target = Users.FirstOrDefault(x => string.Equals(x.Username, value, StringComparison.OrdinalIgnoreCase)); if (target is not null) { AssignedRoles.Clear(); foreach (var role in target.Roles) AssignedRoles.Add(role); } Changed(); ((Command)SetRolesCommand).Raise(); } }
    public string Message { get; private set; } = string.Empty;
    public bool IsBusy { get; private set; }
    public bool CanEditRoles { get; }
    public ObservableCollection<RoleDefinition> RoleCatalog { get; }
    public ObservableCollection<string> AssignedRoles { get; }
    public ObservableCollection<UserAccount> Users { get; } = new();
    public ICommand CreateCommand { get; }
    public ICommand SetRolesCommand { get; }
    public ICommand ToggleRoleCommand { get; }
    public void ToggleRole(string? role) { if (!CanEditRoles || !SystemRoles.TryCanonicalize(role, out var canonical)) return; if (AssignedRoles.Contains(canonical)) AssignedRoles.Remove(canonical); else AssignedRoles.Add(canonical); }
    public async Task LoadUsersAsync() { if (!CanEditRoles) return; try { Users.Clear(); foreach (var user in await _service.ListUsersAsync()) Users.Add(user); } catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); } }
    public async Task CreateAsync()
    {
        if (IsBusy) return; IsBusy = true; Changed(nameof(IsBusy));
        try { var result = await _service.CreateUserAsync(new(Username, DisplayName, InitialPassword)); Message = result.Message; Changed(nameof(Message)); if (result.Succeeded) { Username = string.Empty; DisplayName = string.Empty; } }
        catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); }
        finally { InitialPassword = string.Empty; IsBusy = false; Changed(nameof(IsBusy)); ((Command)CreateCommand).Raise(); }
    }
    public async Task SetRolesAsync()
    {
        if (!CanEditRoles || string.IsNullOrWhiteSpace(SelectedUsername)) { Message = "Only an administrator can change user roles."; Changed(nameof(Message)); return; }
        IsBusy = true; Changed(nameof(IsBusy));
        try { var result = await _service.SetUserRolesAsync(SelectedUsername, AssignedRoles); Message = result.Message; Changed(nameof(Message)); if (result.Succeeded) await LoadUsersAsync(); }
        catch (Exception ex) { Message = ex.Message; Changed(nameof(Message)); }
        finally { IsBusy = false; Changed(nameof(IsBusy)); ((Command)SetRolesCommand).Raise(); }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private sealed class Command(Action<object?> execute, Predicate<object?>? can = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => can?.Invoke(p) ?? true; public void Execute(object? p) => execute(p); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
