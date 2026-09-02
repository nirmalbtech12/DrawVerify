using System.IO;
using System.Windows.Controls;
using Microsoft.Win32;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;
public partial class StepUploadView : UserControl
{
    public StepUploadView() { InitializeComponent(); }
    public void AttachFilePicker(StepUploadViewModel model) => model.SelectRequested += () => { var dialog=new OpenFileDialog { Filter="STEP files (*.step;*.stp)|*.step;*.stp" }; if(dialog.ShowDialog()==true) model.Select(dialog.SafeFileName,File.OpenRead(dialog.FileName)); };
}
