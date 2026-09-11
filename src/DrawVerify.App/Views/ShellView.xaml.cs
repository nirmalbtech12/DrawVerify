using System.Windows;
using System.Windows.Controls;
using DrawVerify.App.Navigation;

namespace DrawVerify.App.Views;

public partial class ShellView : UserControl
{
    public event Action? BackRequested;
    public ShellView() { InitializeComponent(); }
    public void SetScreen(UIElement content, string target) { ContentHost.Content = content; ScreenTitle.Text = ScreenRegistry.Title(target); Breadcrumb.Text = $"Dashboard > {ScreenTitle.Text}"; SetStatus((content as FrameworkElement)?.DataContext); }
    public void SetStatus(string text) => StatusText.Text = text;
    private void SetStatus(object? dataContext) { if (dataContext is null) { SetStatus("Ready"); return; } var busy = dataContext.GetType().GetProperty("IsBusy")?.GetValue(dataContext) as bool?; var message = dataContext.GetType().GetProperty("Message")?.GetValue(dataContext)?.ToString(); var status = dataContext.GetType().GetProperty("MatchingStatus")?.GetValue(dataContext)?.ToString(); SetStatus(busy == true ? "Operation in progress…" : !string.IsNullOrWhiteSpace(message) ? message : !string.IsNullOrWhiteSpace(status) ? status : "Ready"); }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
}
