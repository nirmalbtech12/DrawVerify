using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly UsernamePasswordAuthenticationService _authentication;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _licenseAccepted;
    public LoginViewModel(UsernamePasswordAuthenticationService authentication) { _authentication=authentication; LoginCommand=new Command(async _=>await LoginAsync(), _=>!IsBusy && LicenseAccepted); }
    public event PropertyChangedEventHandler? PropertyChanged; public event Action? LoginSucceeded;
    public string Username { get=>_username; set { _username=value; Changed(); } }
    public string Password { get=>_password; set { _password=value; Changed(); } }
    public bool LicenseAccepted { get=>_licenseAccepted; set { _licenseAccepted=value; Changed(); ((Command)LoginCommand).Raise(); } }
    public string Message { get; private set; } = string.Empty; public bool IsBusy { get; private set; }
    public ICommand LoginCommand { get; }
    public async Task LoginAsync()
    {
        if (!LicenseAccepted) { Message="You must accept the DrawVerify license terms before signing in."; Changed(nameof(Message)); return; }
        if (IsBusy) return; IsBusy=true; Changed(nameof(IsBusy)); try { var result=await _authentication.LoginAsync(Username,Password); Message=result.Message; Changed(nameof(Message)); if(result.Succeeded) LoginSucceeded?.Invoke(); } catch(Exception) { Message="Unable to complete login."; Changed(nameof(Message)); } finally { Password=string.Empty; Changed(nameof(Password)); IsBusy=false; Changed(nameof(IsBusy)); ((Command)LoginCommand).Raise(); }
    }
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
    private sealed class Command(Action<object?> execute, Predicate<object?>? can=null):ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>can?.Invoke(p)??true; public void Execute(object? p)=>execute(p); public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty); }
}
