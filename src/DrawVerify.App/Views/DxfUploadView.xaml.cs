using System.IO;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using Microsoft.Win32;

namespace DrawVerify.App.Views;

public partial class DxfUploadView : UserControl
{
    public DxfUploadView() => InitializeComponent();

    public void AttachFilePicker(DxfUploadViewModel model) => model.SelectRequested += () =>
    {
        var dialog = new OpenFileDialog { Filter = DxfUploadViewModel.PickerFilter, CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true)
            model.Select(dialog.SafeFileName, File.OpenRead(dialog.FileName));
    };
}
