using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrawVerify.App.Navigation;
using DrawVerify.App.Services;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App;

public partial class MainWindow : Window
{
    private ShellView? _shell;
    private NavigationService? _navigation;
    private readonly NavigationHistory _history = new();
    public Func<MessageBoxResult> DiscardConfirmation { get; set; } = () => MessageBox.Show("Discard unsaved changes?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    public MainWindow()
    {
        CompositionRoot.Initialize(); InitializeComponent();
        if (System.Windows.Application.Current?.Resources["BrandIconSource"] is System.Windows.Media.ImageSource icon) Icon = icon;
        Loaded += async (_, _) => await ApplySystemRetentionAsync(); ShowLogin();
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => GoBack()), Key.Left, ModifierKeys.Alt));
    }
    private static async Task ApplySystemRetentionAsync() { try { await CompositionRoot.ApplySystemRetentionAsync(); } catch (Exception ex) { new CentralizedLogger().Log(ex, "System audit retention", "Unable to apply the configured audit retention policy."); } }
    private void ShowLogin()
    {
        _navigation = null; _history.Clear(); _shell = null;
        var view = new LoginView { DataContext = new LoginViewModel(CompositionRoot.CreateAuthenticationService()) }; ((LoginViewModel)view.DataContext).LoginSucceeded += ShowAuthenticatedContent; MainContent.Content = view;
    }
    private void ShowAuthenticatedContent()
    {
        if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; }
        var requestedTarget = _navigation?.CurrentTarget;
        _history.Clear();
        _shell = new ShellView { DataContext = new ShellViewModel(CompositionRoot.SessionAuthentication) };
        _shell.BackRequested += GoBack;
        _navigation = new NavigationService(new ScreenAccessGuard(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization), _history, (content, target) => _shell.SetScreen(content, target), Denied);
        _shell.ApplicationMenu.Items.Clear(); foreach (var item in new RoleMenuBuilder(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization).Build(Navigate, LogoutAsync, Denied)) _shell.ApplicationMenu.Items.Add(item);
        MainContent.Content = _shell;
        var guard = new ScreenAccessGuard(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization);
        if (requestedTarget is not null && guard.CanAccess(requestedTarget)) Navigate(requestedTarget); else Navigate(ScreenRegistry.Dashboard);
    }
    private void Navigate(string target) => _navigation?.Navigate(target, () => CreateScreen(target));
    private UIElement CreateScreen(string target)
    {
        if (target == ScreenRegistry.Dashboard) return new DashboardView { DataContext = new DashboardViewModel(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization) };
        if (target == ScreenRegistry.UploadVerify) { var view = new CombinedUploadVerifyView { DataContext = CompositionRoot.CreateCombinedUploadVerifyViewModel() }; view.AttachFilePickers((CombinedUploadVerifyViewModel)view.DataContext); view.BackRequested += () => HandleTaskBack((TaskScreenViewModel)view.DataContext); return view; }
        if (target == ScreenRegistry.StepUpload) { var view = new StepUploadView { DataContext = CompositionRoot.CreateStepUploadViewModel() }; view.AttachFilePicker((StepUploadViewModel)view.DataContext); view.BackRequested += () => HandleTaskBack((TaskScreenViewModel)view.DataContext); return view; }
        if (target == ScreenRegistry.DxfUpload) { var view = new DxfUploadView { DataContext = CompositionRoot.CreateDxfUploadViewModel() }; view.AttachFilePicker((DxfUploadViewModel)view.DataContext); view.BackRequested += () => HandleTaskBack((TaskScreenViewModel)view.DataContext); return view; }
        if (target == ScreenRegistry.Verification) { var view = new VerificationView { DataContext = new VerificationViewModel(CompositionRoot.SessionAuthentication, CompositionRoot.RoleAuthorization, CompositionRoot.CreateDimensionMatchingService(), new CentralizedLogger(), CompositionRoot.ConfiguredDimensionMatching) }; view.BackRequested += () => HandleTaskBack((TaskScreenViewModel)view.DataContext); return view; }
        if (target == ScreenRegistry.Workflow) { var view = new WorkflowView { DataContext = new WorkflowViewModel() }; view.BackRequested += () => HandleTaskBack((TaskScreenViewModel)view.DataContext); return view; }
        if (target == ScreenRegistry.UserAdministration) return CreateAdministrationView();
        if (target == ScreenRegistry.AuditLog) return new AuditLogView { DataContext = new AuditLogViewModel(CompositionRoot.CreateAuditLogService()) };
        if (target == ScreenRegistry.Report) return new ReportView { DataContext = new ReportViewModel(CompositionRoot.CreateReportService(), new CentralizedLogger()) };
        if (target == ScreenRegistry.Help) return new Border { Child = new TextBlock { Text = "Help\n\nUse the menu to navigate to the screens allowed for your role.", TextWrapping = TextWrapping.Wrap, FontSize = 16 } };
        throw new InvalidOperationException($"No screen is registered for navigation target '{target}'.");
    }
    private UserControl CreateAdministrationView()
    {
        var view = new UserAdministrationView { DataContext = new UserAdministrationViewModel(CompositionRoot.CreateUserAdministrationService()) }; var model = (UserAdministrationViewModel)view.DataContext;
        model.AuthorizationStateChanged += RefreshAuthenticatedShell; model.CancelRequested += () => { if (!model.IsCreationDirty || DiscardConfirmation() == MessageBoxResult.Yes) { model.DiscardCreation(); Navigate(ScreenRegistry.Dashboard); } }; model.BackRequested += () => { if (!model.IsCreationDirty || DiscardConfirmation() == MessageBoxResult.Yes) { model.DiscardCreation(); Navigate(ScreenRegistry.Dashboard); } }; model.RoleCancelRequested += () => Navigate(ScreenRegistry.Dashboard); model.RoleBackRequested += () => Navigate(ScreenRegistry.Dashboard); model.RoleConfirmation = message => MessageBox.Show(message, "Confirm role changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes; return view;
    }
    private void RefreshAuthenticatedShell() => ShowAuthenticatedContent();
    private void HandleTaskBack(TaskScreenViewModel model)
    {
        if (model.IsDirty && DiscardConfirmation() != MessageBoxResult.Yes) return;
        GoBack();
    }
    private void ShowUserAdministration() { if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; } if (_shell is null) ShowAuthenticatedContent(); Navigate(ScreenRegistry.UserAdministration); }
    private void ShowUpload() { if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; } Navigate(ScreenRegistry.StepUpload); }
    private void ShowDxfUpload() { if (!CompositionRoot.SessionAuthentication.IsAuthenticated) { ShowLogin(); return; } Navigate(ScreenRegistry.DxfUpload); }
    private async Task LogoutAsync()
    {
        try { var result = await CompositionRoot.CreateAuthenticationService().LogoutAsync(); if (!result.Succeeded) { Denied(result.Message); return; } _history.Clear(); ShowLogin(); }
        catch (Exception ex) { new CentralizedLogger().Log(ex, "WPF logout", "Unable to complete logout."); MessageBox.Show("Unable to complete logout.", "Logout failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void GoBack() { if (_navigation is not null) _navigation.GoBack(CreateScreen); }
    private void Denied(string message) => MessageBox.Show(message, "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
    private sealed class RelayCommand(Action<object?> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => true; public void Execute(object? p) => execute(p); }
}
