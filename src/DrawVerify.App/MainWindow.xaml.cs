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
        ShowLogin();
    }

    private void ShowAuthenticatedContent()
    {
        if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; }
        var panel=new System.Windows.Controls.StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text=$"Signed in as {CompositionRoot.SessionAuthentication.CurrentUser!.Username}", FontSize=20 });
        var upload=new System.Windows.Controls.Button { Content="Upload model", Margin=new Thickness(0,16,0,8) }; upload.Click += (_,_)=>ShowUpload(); panel.Children.Add(upload);
        if(CompositionRoot.SessionAuthentication.CurrentUser.HasRole("Admin")) { var users=new System.Windows.Controls.Button { Content="Create user", Margin=new Thickness(0,0,0,8) }; users.Click += (_,_)=>ShowUserAdministration(); panel.Children.Add(users); }
        var logout=new System.Windows.Controls.Button { Content="Logout" }; logout.Click += async (_,_)=>{ try { await CompositionRoot.CreateAuthenticationService().LogoutAsync(); ShowLogin(); } catch(Exception ex) { System.Windows.MessageBox.Show(ex.Message,"Logout failed"); } }; panel.Children.Add(logout); MainContent.Content=panel;
    }

    private void ShowLogin() { var view=new LoginView { DataContext=new LoginViewModel(CompositionRoot.CreateAuthenticationService()) }; ((LoginViewModel)view.DataContext).LoginSucceeded += ShowAuthenticatedContent; MainContent.Content=view; }
    private void ShowUserAdministration() { if(!CompositionRoot.SessionAuthentication.IsAuthenticated || !CompositionRoot.SessionAuthentication.CurrentUser!.HasRole("Admin")){ShowLogin();return;} MainContent.Content=new UserAdministrationView { DataContext=CompositionRoot.CreateUserAdministrationService() }; }

    private void ShowUpload()
    {
        if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; }
        var view=new StepUploadView { DataContext=CompositionRoot.CreateStepUploadViewModel() };
        var model=(StepUploadViewModel)view.DataContext;
        view.AttachFilePicker(model);
        model.DocumentUploaded += document => Dispatcher.Invoke(() => MainContent.Content = new DocumentDetailView { DataContext = new DocumentDetailViewModel(document) });
        MainContent.Content=view;
    }
}
