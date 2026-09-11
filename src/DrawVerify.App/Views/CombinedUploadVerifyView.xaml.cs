using System.IO;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using Microsoft.Win32;

namespace DrawVerify.App.Views;

public partial class CombinedUploadVerifyView : UserControl
{
    public event Action? BackRequested;
    public CombinedUploadVerifyView() { InitializeComponent(); Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose(); }
    public void AttachFilePickers(CombinedUploadVerifyViewModel model)
    {
        model.StepUpload.SelectRequested += () => { var dialog = new OpenFileDialog { Filter = "STEP files (*.step;*.stp)|*.step;*.stp", Multiselect = false }; if (dialog.ShowDialog() == true) model.AttachStepSelection(dialog.SafeFileName, File.OpenRead(dialog.FileName)); };
        model.DxfUpload.SelectRequested += () => { var dialog = new OpenFileDialog { Filter = DxfUploadViewModel.PickerFilter, Multiselect = false }; if (dialog.ShowDialog() == true) model.AttachDxfSelection(dialog.SafeFileName, File.OpenRead(dialog.FileName)); };
    }
    private void BackClick(object sender, System.Windows.RoutedEventArgs e) => BackRequested?.Invoke();
}
