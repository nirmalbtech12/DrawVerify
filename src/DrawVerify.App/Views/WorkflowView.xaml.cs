using System.Windows.Controls;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class WorkflowView : UserControl
{
    public event Action? BackRequested;
    public WorkflowView()
    {
        InitializeComponent();
        Loaded += AttachDirtyTracking;
    }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
    private async void QueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DrawVerify.Core.Models.WorkflowQueueItem item && DataContext is WorkflowViewModel model)
            await model.OpenReviewAsync(item);
    }
    private void FindingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DrawVerify.Core.Models.ReviewFinding finding && DataContext is WorkflowViewModel model)
            model.SelectVerificationFinding(finding);
    }
    private async void MakerPackageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DrawVerify.Core.Models.ReviewPackage package && DataContext is WorkflowViewModel model)
            await model.SelectMakerPackageAsync(package);
    }
    private void AttachDirtyTracking(object sender, System.Windows.RoutedEventArgs e) => AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(UserInputChanged));
    private void UserInputChanged(object sender, TextChangedEventArgs e) => (DataContext as TaskScreenViewModel)?.MarkDirtyFromUserInput();
}
