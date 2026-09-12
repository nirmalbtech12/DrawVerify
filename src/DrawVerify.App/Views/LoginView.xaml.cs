using System.Windows.Controls;
using System.Windows;
using System.Windows.Navigation;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;
public partial class LoginView : UserControl
{
    public LoginView() { InitializeComponent(); }
    private void PasswordChanged(object sender, System.Windows.RoutedEventArgs e) { if(DataContext is LoginViewModel model && sender is PasswordBox box) model.Password=box.Password; }
    private void LicenseLinkRequested(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.IsAbsoluteUri)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        else if (string.Equals(e.Uri.OriginalString, "LICENSE.txt", StringComparison.OrdinalIgnoreCase))
        {
            new LicenseViewerWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        e.Handled = true;
    }
}
