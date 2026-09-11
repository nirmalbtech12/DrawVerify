using System.ComponentModel;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.ViewModels;

public sealed class ShellViewModel(ISessionAuthenticationService session) : INotifyPropertyChanged
{
    public string Username => session.CurrentUser?.DisplayName ?? session.CurrentUser?.Username ?? string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Username)));
}
