using System.Windows;
using Microsoft.Win32;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class ReportTemplateConfigurationView : System.Windows.Controls.UserControl
{
    public ReportTemplateConfigurationView() => InitializeComponent();

    private void SelectLogo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportTemplateConfigurationViewModel model || !model.CanEdit) return;
        var dialog = new OpenFileDialog { Title = "Select report template logo", Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true) model.SelectLogo(dialog.FileName);
    }
}
