using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.App.Services;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class LoginViewModel
{
    private readonly SessionAuthenticationService _session;
    public LoginViewModel(SessionAuthenticationService session) { _session=session; SignInCommand=new Command(SignIn); }
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = RoleNames.Maker;
    public string Message { get; private set; } = string.Empty;
    public ICommand SignInCommand { get; }
    public event Action? SignedIn;
    private void SignIn() { if(string.IsNullOrWhiteSpace(UserId)||string.IsNullOrWhiteSpace(DisplayName)){Message="User ID and display name are required.";return;} _session.SignIn(new User {UserId=UserId.Trim(),DisplayName=DisplayName.Trim(),Role=Role.Trim()});SignedIn?.Invoke(); }
    private sealed class Command(Action execute):ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter)=>true; public void Execute(object? parameter)=>execute(); }
    private static class RoleNames { public const string Maker = "Maker"; }
}
