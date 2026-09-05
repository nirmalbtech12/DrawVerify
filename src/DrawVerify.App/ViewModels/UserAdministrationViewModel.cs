using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;

namespace DrawVerify.App.ViewModels;

public sealed class UserAdministrationViewModel : INotifyPropertyChanged
{
    private readonly UserAdministrationService _service; private string _username=string.Empty; private string _displayName=string.Empty; private string _password=string.Empty;
    public UserAdministrationViewModel(UserAdministrationService service) { _service=service; CreateCommand=new Command(async _=>await CreateAsync(), _=>!IsBusy); }
    public event PropertyChangedEventHandler? PropertyChanged; public string Username { get=>_username; set{_username=value;Changed();} } public string DisplayName { get=>_displayName; set{_displayName=value;Changed();} } public string InitialPassword { get=>_password; set{_password=value;Changed();} }
    public string Message { get; private set; }=string.Empty; public bool IsBusy { get; private set; } public ICommand CreateCommand { get; }
    public async Task CreateAsync() { if(IsBusy)return; IsBusy=true;Changed(nameof(IsBusy));try{var result=await _service.CreateUserAsync(new(Username,DisplayName,InitialPassword));Message=result.Message;Changed(nameof(Message));if(result.Succeeded){Username=string.Empty;DisplayName=string.Empty;InitialPassword=string.Empty;}}catch(Exception){Message="Unable to create the user.";Changed(nameof(Message));}finally{IsBusy=false;Changed(nameof(IsBusy));((Command)CreateCommand).Raise();} }
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n)); private sealed class Command(Action<object?> execute,Predicate<object?>? can=null):ICommand{public event EventHandler? CanExecuteChanged;public bool CanExecute(object? p)=>can?.Invoke(p)??true;public void Execute(object? p)=>execute(p);public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);}
}
