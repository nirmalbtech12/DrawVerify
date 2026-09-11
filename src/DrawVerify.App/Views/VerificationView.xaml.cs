using System.Windows.Controls;

namespace DrawVerify.App.Views;

public partial class VerificationView : UserControl
{
    public event Action? BackRequested;
    public VerificationView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
}
