using System.Windows.Controls;

namespace DrawVerify.App.Views;

public partial class WorkflowView : UserControl
{
    public event Action? BackRequested;
    public WorkflowView()
    {
        InitializeComponent();
    }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
}
