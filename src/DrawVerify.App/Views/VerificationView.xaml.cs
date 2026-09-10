using System.Windows.Controls;

namespace DrawVerify.App.Views;

public partial class VerificationView : UserControl
{
    public VerificationView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
