using System.Windows;
using DrawVerify.App.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.Core.Authorization;

namespace DrawVerify.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        CompositionRoot.Initialize();
        InitializeComponent();
        Loaded += async (_, _) => await ApplySystemRetentionAsync();
        ShowLogin();
    }

    private static async Task ApplySystemRetentionAsync()
    {
        try { await CompositionRoot.ApplySystemRetentionAsync(); }
        catch (Exception) { System.Windows.MessageBox.Show("Unable to apply the configured audit retention policy.", "Retention failed"); }
    }

    private void ShowAuthenticatedContent()
    {
        if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; }
        var panel=new System.Windows.Controls.StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text=$"Signed in as {CompositionRoot.SessionAuthentication.CurrentUser!.Username}", FontSize=20 });
        var upload=new System.Windows.Controls.Button { Content="Upload model", Margin=new Thickness(0,16,0,8), IsEnabled=CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.Upload) }; upload.Click += (_,_)=>ShowUpload(); panel.Children.Add(upload);
        var review=new System.Windows.Controls.Button { Content="Review verification", Margin=new Thickness(0,0,0,8) }; review.Click += (_,_)=>ShowVerificationReview(); review.IsEnabled=CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.ReviewVerification); panel.Children.Add(review);
        var profile=new System.Windows.Controls.Button { Content="My profile", Margin=new Thickness(0,0,0,8) }; profile.Click += (_,_)=>ShowProfile(); panel.Children.Add(profile);
        if(CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.ManageUsersAndRoles)) { var users=new System.Windows.Controls.Button { Content="Create user", Margin=new Thickness(0,0,0,8) }; users.Click += (_,_)=>ShowUserAdministration(); panel.Children.Add(users); }
        if(CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.ViewAuditLog)) { var audit=new System.Windows.Controls.Button { Content="Audit log", Margin=new Thickness(0,0,0,8) }; audit.Click += (_,_)=>ShowAuditLog(); panel.Children.Add(audit); }
        var logout=new System.Windows.Controls.Button { Content="Logout" }; logout.Click += async (_,_)=>{ try { await CompositionRoot.CreateAuthenticationService().LogoutAsync(); ShowLogin(); } catch(Exception ex) { new CentralizedLogger().Log(ex,"WPF logout","Unable to complete logout."); System.Windows.MessageBox.Show("Unable to complete logout.","Logout failed"); } }; panel.Children.Add(logout); MainContent.Content=panel;
    }

    private void ShowLogin() { var view=new LoginView { DataContext=new LoginViewModel(CompositionRoot.CreateAuthenticationService()) }; ((LoginViewModel)view.DataContext).LoginSucceeded += ShowAuthenticatedContent; MainContent.Content=view; }
    private void ShowUserAdministration() { if(!CompositionRoot.SessionAuthentication.IsAuthenticated || !CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.ManageUsersAndRoles)){ShowLogin();return;} var view = new UserAdministrationView { DataContext=new UserAdministrationViewModel(CompositionRoot.CreateUserAdministrationService()) }; ((UserAdministrationViewModel)view.DataContext).AuthorizationStateChanged += ShowAuthenticatedContent; MainContent.Content=view; }
    private void ShowProfile() { if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; } var view = new UserAdministrationView { DataContext = new UserAdministrationViewModel(CompositionRoot.CreateUserAdministrationService()) }; ((UserAdministrationViewModel)view.DataContext).AuthorizationStateChanged += ShowAuthenticatedContent; MainContent.Content = view; }
    private void ShowAuditLog() { if (!CompositionRoot.SessionAuthentication.IsAuthenticated || !CompositionRoot.RoleAuthorization.IsAllowed(CompositionRoot.SessionAuthentication.CurrentUser, RolePermission.ViewAuditLog)) { ShowLogin(); return; } MainContent.Content = new AuditLogView { DataContext = new AuditLogViewModel(CompositionRoot.CreateAuditLogService()) }; }

    private void ShowUpload()
    {
        if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; }
        var view=new StepUploadView { DataContext=CompositionRoot.CreateStepUploadViewModel() };
        var model=(StepUploadViewModel)view.DataContext;
        view.AttachFilePicker(model);
        model.DocumentUploaded += document => Dispatcher.Invoke(() => ShowProtected(() => new DocumentDetailView { DataContext = new DocumentDetailViewModel(document) }));
        MainContent.Content=view;
    }
    private void ShowVerificationReview()
    {
        var model = new VerificationViewModel(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization);
        if (!model.TryEnterReview(out var message)) { System.Windows.MessageBox.Show(message, "Access denied"); return; }
        MainContent.Content = new VerificationView { DataContext = model };
    }
    private void ShowProtected(Func<UIElement> contentFactory) { if(!CompositionRoot.SessionAuthentication.IsAuthenticated){ShowLogin();return;} MainContent.Content=contentFactory(); }
}
