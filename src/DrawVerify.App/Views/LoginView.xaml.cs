using System.Windows.Controls;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;
public partial class LoginView : UserControl
{
    public LoginView() { InitializeComponent(); }
    private void PasswordChanged(object sender, System.Windows.RoutedEventArgs e) { if(DataContext is LoginViewModel model && sender is PasswordBox box) model.Password=box.Password; }
}
