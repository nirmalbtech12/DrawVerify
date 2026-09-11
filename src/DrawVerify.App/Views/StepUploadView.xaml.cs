using System.IO;
using System.Windows.Controls;
using Microsoft.Win32;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;
public partial class StepUploadView : UserControl
{
    public event Action? BackRequested;
    public StepUploadView() { InitializeComponent(); Loaded += AttachDirtyTracking; MaterializeVisualTree(); }
    public void AttachFilePicker(StepUploadViewModel model) => model.SelectRequested += () => { var dialog=new OpenFileDialog { Filter="STEP files (*.step;*.stp)|*.step;*.stp" }; if(dialog.ShowDialog()==true) model.Select(dialog.SafeFileName,File.OpenRead(dialog.FileName)); };
    private void MaterializeVisualTree() { Measure(new System.Windows.Size(1200, 800)); Arrange(new System.Windows.Rect(0, 0, 1200, 800)); }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
    private void AttachDirtyTracking(object sender, System.Windows.RoutedEventArgs e) => AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(UserInputChanged));
    private void UserInputChanged(object sender, TextChangedEventArgs e) => (DataContext as TaskScreenViewModel)?.MarkDirtyFromUserInput();
}
