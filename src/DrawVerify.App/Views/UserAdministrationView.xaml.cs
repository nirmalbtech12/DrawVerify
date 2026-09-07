using System.Windows.Controls;
using DrawVerify.App.ViewModels;
namespace DrawVerify.App.Views;
public partial class UserAdministrationView : UserControl
{
    public UserAdministrationView() { InitializeComponent(); }
    private async void ViewLoaded(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is UserAdministrationViewModel model) { model.CreationFormReset += ClearInitialPassword; await model.LoadUsersAsync(); } }
    private void ClearInitialPassword() => InitialPasswordBox.Clear();
    private void PasswordChanged(object sender, System.Windows.RoutedEventArgs e) { if(DataContext is UserAdministrationViewModel model && sender is PasswordBox box) model.InitialPassword=box.Password; }
    private void ResetPasswordChanged(object sender, System.Windows.RoutedEventArgs e) { if(DataContext is UserAdministrationViewModel model && sender is PasswordBox box) model.ResetPassword=box.Password; }
}
