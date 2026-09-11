using System.Windows.Controls;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class VerificationView : UserControl
{
    public event Action? BackRequested;
    public VerificationView()
    {
        InitializeComponent();
        Loaded += AttachDirtyTracking;
        Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
    private void AttachDirtyTracking(object sender, System.Windows.RoutedEventArgs e) => AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(UserInputChanged));
    private void UserInputChanged(object sender, TextChangedEventArgs e) => (DataContext as TaskScreenViewModel)?.MarkDirtyFromUserInput();
}
