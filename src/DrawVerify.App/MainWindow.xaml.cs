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
        if (!CompositionRoot.Authentication.IsAuthenticated)
        {
            var login = new LoginView { DataContext = new LoginViewModel(CompositionRoot.Authentication) };
            ((LoginViewModel)login.DataContext).SignedIn += () => Dispatcher.Invoke(ShowAuthenticatedContent);
            MainContent.Content = login;
            return;
        }
        ShowAuthenticatedContent();
    }

    private void ShowAuthenticatedContent()
    {
        if (!CompositionRoot.Authentication.IsMaker)
        {
            MainContent.Content = new System.Windows.Controls.TextBlock { Text = "Access denied. A Maker role is required to upload STEP files.", Margin = new System.Windows.Thickness(24) };
            return;
        }
        ShowUpload();
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
