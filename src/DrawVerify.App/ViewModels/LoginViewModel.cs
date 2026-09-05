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
    public LoginViewModel(UsernamePasswordAuthenticationService authentication) { _authentication=authentication; LoginCommand=new Command(async _=>await LoginAsync(), _=>!IsBusy); }
    public event PropertyChangedEventHandler? PropertyChanged; public event Action? LoginSucceeded;
    public string Username { get=>_username; set { _username=value; Changed(); } }
    public string Password { get=>_password; set { _password=value; Changed(); } }
    public string Message { get; private set; } = string.Empty; public bool IsBusy { get; private set; }
    public ICommand LoginCommand { get; }
    public async Task LoginAsync()
    {
        if (IsBusy) return; IsBusy=true; Changed(nameof(IsBusy)); try { var result=await _authentication.LoginAsync(Username,Password); Message=result.Message; Changed(nameof(Message)); if(result.Succeeded) { Password=string.Empty; LoginSucceeded?.Invoke(); } } catch(Exception) { Message="Unable to complete login."; Changed(nameof(Message)); } finally { IsBusy=false; Changed(nameof(IsBusy)); ((Command)LoginCommand).Raise(); }
    }
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
    private sealed class Command(Action<object?> execute, Predicate<object?>? can=null):ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>can?.Invoke(p)??true; public void Execute(object? p)=>execute(p); public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty); }
}
