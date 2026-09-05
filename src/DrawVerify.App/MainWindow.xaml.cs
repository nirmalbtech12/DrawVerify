using System.Windows;
using DrawVerify.App.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;

namespace DrawVerify.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        CompositionRoot.Initialize();
        InitializeComponent();
        ShowUpload();
    }

    private void ShowAuthenticatedContent()
    {
    }

    private void ShowUpload()
    {
        var view=new StepUploadView { DataContext=CompositionRoot.CreateStepUploadViewModel() };
        var model=(StepUploadViewModel)view.DataContext;
        view.AttachFilePicker(model);
        model.DocumentUploaded += document => Dispatcher.Invoke(() => MainContent.Content = new DocumentDetailView { DataContext = new DocumentDetailViewModel(document) });
        MainContent.Content=view;
    }
}
