using System.Security.Principal;
using System.Windows.Input;
using DrawVerify.App.Services;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class LoginViewModel
{
    private readonly SessionAuthenticationService _session;
    public LoginViewModel(SessionAuthenticationService session) { _session=session; SignInCommand=new Command(SignIn); }
    public string Message { get; private set; } = string.Empty;
    public ICommand SignInCommand { get; }
    public event Action? SignedIn;
    private void SignIn()
    {
        using var identity=WindowsIdentity.GetCurrent();
        if(identity is null) { Message="A trusted Windows identity is required."; return; }
        var principal=new WindowsPrincipal(identity);
        var isMaker=principal.IsInRole("DrawVerify-Makers");
        _session.SignIn(new User { UserId=identity.Name, DisplayName=identity.Name, Role=isMaker ? "Maker" : "Reviewer" });
        SignedIn?.Invoke();
    }
    private sealed class Command(Action execute):ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter)=>true; public void Execute(object? parameter)=>execute(); }
}
