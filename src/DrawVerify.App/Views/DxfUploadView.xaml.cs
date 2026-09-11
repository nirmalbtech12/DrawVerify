using System.IO;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using Microsoft.Win32;

namespace DrawVerify.App.Views;

public partial class DxfUploadView : UserControl
{
    public event Action? BackRequested;
    public DxfUploadView() { InitializeComponent(); Loaded += AttachDirtyTracking; Measure(new System.Windows.Size(1200, 800)); Arrange(new System.Windows.Rect(0, 0, 1200, 800)); }

    public void AttachFilePicker(DxfUploadViewModel model) => model.SelectRequested += () =>
    {
        var dialog = new OpenFileDialog { Filter = DxfUploadViewModel.PickerFilter, CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true)
            model.Select(dialog.SafeFileName, File.OpenRead(dialog.FileName));
    };
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
    private void AttachDirtyTracking(object sender, System.Windows.RoutedEventArgs e) => AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(UserInputChanged));
    private void UserInputChanged(object sender, TextChangedEventArgs e) => (DataContext as TaskScreenViewModel)?.MarkDirtyFromUserInput();
}
