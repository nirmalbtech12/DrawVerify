using System.Windows.Controls;

namespace DrawVerify.App.Views;

public partial class DashboardView : UserControl
{
    public event Action<string>? QuickLinkRequested;
    public DashboardView()
    {
        InitializeComponent();
    }
    private void QuickLinkClick(object sender, System.Windows.RoutedEventArgs e) { if (sender is Button button && button.Tag is string target) QuickLinkRequested?.Invoke(target); }
}
