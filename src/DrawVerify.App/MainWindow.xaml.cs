using System.Windows;
using DrawVerify.App.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;

namespace DrawVerify.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var view=new StepUploadView { DataContext=CompositionRoot.CreateStepUploadViewModel() };
        view.AttachFilePicker((StepUploadViewModel)view.DataContext);
        MainContent.Content=view;
    }
}
