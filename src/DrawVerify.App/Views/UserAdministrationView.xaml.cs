using System.Windows.Controls;
using DrawVerify.App.ViewModels;
namespace DrawVerify.App.Views;
public partial class UserAdministrationView : UserControl
{
    public UserAdministrationView() { InitializeComponent(); }
    private async void ViewLoaded(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is UserAdministrationViewModel model) await model.LoadUsersAsync(); }
    private void PasswordChanged(object sender, System.Windows.RoutedEventArgs e) { if(DataContext is UserAdministrationViewModel model && sender is PasswordBox box) model.InitialPassword=box.Password; }
}
