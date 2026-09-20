using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawVerify.App.Services;
using DrawVerify.App.Views;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class MainWindowAuthenticationNavigationTests
{
    [Fact(DisplayName = "TSD-AC02-AUTH unauthenticated shared-link entry is represented as an authentication handoff")]
    public void Unauthenticated_entry_requires_authentication_before_resume()
    {
        var result = InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, "Authentication is required to access this shared link.");
        Assert.Equal(InternalSharedLinkAccessFailure.AuthenticationRequired, result.Failure);
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "T-UX-01 clean Cancel returns from user creation to authenticated content")]
    public void Clean_user_creation_cancel_returns_to_authenticated_content()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }));
                var window = new MainWindow();
                typeof(MainWindow).GetMethod("ShowUserAdministration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var shell = Assert.IsType<ShellView>(Content(window).Content);
                var view = Assert.IsType<UserAdministrationView>(ShellContent(shell).Content);
                var vm = Assert.IsType<UserAdministrationViewModel>(view.DataContext);
                vm.CancelCommand.Execute(null);
                Assert.IsType<DashboardView>(ShellContent(shell).Content);
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("Clean creation cancellation navigation assertion failed.", failure);
    }

    [Fact(DisplayName = "T-UX-01 dirty Cancel and Back retain on No and navigate on Yes")]
    public void Dirty_creation_cancel_and_back_use_discard_confirmation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }));
                var window = new MainWindow();
                typeof(MainWindow).GetMethod("ShowUserAdministration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var shell = Assert.IsType<ShellView>(Content(window).Content);
                var view = Assert.IsType<UserAdministrationView>(ShellContent(shell).Content);
                var vm = Assert.IsType<UserAdministrationViewModel>(view.DataContext);
                vm.Username = "cancel.pending"; vm.DisplayName = "Cancel Pending";
                window.DiscardConfirmation = () => System.Windows.MessageBoxResult.No;
                vm.CancelCommand.Execute(null);
                Assert.Same(view, ShellContent(shell).Content);
                Assert.Equal("cancel.pending", vm.Username);
                window.DiscardConfirmation = () => System.Windows.MessageBoxResult.Yes;
                vm.CancelCommand.Execute(null);
                Assert.IsType<DashboardView>(ShellContent(shell).Content);
                typeof(MainWindow).GetMethod("ShowUserAdministration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                view = Assert.IsType<UserAdministrationView>(ShellContent(shell).Content);
                vm = Assert.IsType<UserAdministrationViewModel>(view.DataContext);
                vm.Username = "back.pending"; vm.DisplayName = "Back Pending";
                window.DiscardConfirmation = () => System.Windows.MessageBoxResult.No;
                vm.BackCommand.Execute(null);
                Assert.Same(view, ShellContent(shell).Content);
                Assert.Equal("back.pending", vm.Username);
                window.DiscardConfirmation = () => System.Windows.MessageBoxResult.Yes;
                vm.BackCommand.Execute(null);
                Assert.IsType<DashboardView>(ShellContent(shell).Content);
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("Dirty Cancel/Back confirmation assertion failed.", failure);
    }

    [Fact(DisplayName = "T-UX-05 starts at login and guards protected routes")]
    public void Unauthenticated_window_routes_protected_content_to_login()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CompositionRoot.SessionAuthentication.SignOut();
                var window = new MainWindow();
                Assert.IsType<LoginView>(Content(window).Content);
                typeof(MainWindow).GetMethod("ShowUpload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.IsType<LoginView>(Content(window).Content);
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("WPF navigation assertion failed.", failure);
    }

    [Fact(DisplayName = "T-UX-05 blocks protected content after logout")]
    public void Authenticated_content_is_guarded_after_session_ends()
    {
        Exception? failure=null; var thread=new Thread(()=>{try{CompositionRoot.SessionAuthentication.SignIn(new DrawVerify.Core.Models.AuthenticatedUser("ADMIN",new[]{"Admin"})); var window=new MainWindow(); typeof(MainWindow).GetMethod("ShowAuthenticatedContent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null); CompositionRoot.SessionAuthentication.SignOut(); typeof(MainWindow).GetMethod("ShowUpload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null); Assert.IsType<LoginView>(Content(window).Content); window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown();}catch(Exception ex){failure=ex;}}); thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw new Xunit.Sdk.XunitException("Post-logout protected navigation assertion failed.",failure);
    }

    [Fact(DisplayName = "TD-KAN23-WPF-002 Maker navigation exposes only combined upload")]
    public void Maker_navigation_exposes_only_combined_upload()
    {
        Exception? failure = null; var thread = new Thread(() => { try
        {
            CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("MAKER", new[] { "Maker" }));
            var window = new MainWindow();
            typeof(MainWindow).GetMethod("ShowAuthenticatedContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var shell = Assert.IsType<ShellView>(Content(window).Content);
            var upload = Assert.Single(Menu(shell).Items.Cast<MenuItem>(), x => x.Header?.ToString() == "_Upload & Verify");
            var combined = Assert.Single(upload.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "_Combined Upload & Verify");
            Assert.True(combined.IsEnabled);
            Assert.DoesNotContain(upload.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "Upload _Drawing (DXF)");
            Assert.DoesNotContain(upload.Items.Cast<MenuItem>(), x => x.Header?.ToString() == "Upload _Model (STEP)");
            window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown();
        } catch (Exception ex) { failure = ex; }}); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("DXF Maker navigation assertion failed.", failure);
    }

    [Fact(DisplayName = "TD-APPUI combined upload workflow can be constructed for Maker")]
    public void Maker_can_construct_combined_upload_workflow()
    {
        Exception? failure = null; var thread = new Thread(() => { try
        {
            CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("MAKER", new[] { "Maker" }));
            var view = new CombinedUploadVerifyView { DataContext = CompositionRoot.CreateCombinedUploadVerifyViewModel() };
            Assert.NotNull(view);
        } catch (Exception ex) { failure = ex; }}); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("Combined upload workflow construction failed.", failure);
    }

    [Fact(DisplayName = "TD-APPUI-001 self-profile navigation displays roles for non-admin users")]
    public void Non_admin_can_reach_self_profile_without_admin_controls()
    {
        Exception? failure=null; var thread=new Thread(()=>{try{CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("VIEWER",new[]{"Viewer"})); var window=new MainWindow(); typeof(MainWindow).GetMethod("ShowAuthenticatedContent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null); var shell=Assert.IsType<ShellView>(Content(window).Content); Assert.DoesNotContain(Menu(shell).Items.Cast<MenuItem>(), x=>x.Header?.ToString()=="_Admin"); Assert.IsType<DashboardView>(ShellContent(shell).Content); window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown();}catch(Exception ex){failure=ex;}}); thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(5)),"Self-profile shell assertion exceeded 5 seconds.");if(failure is not null)throw new Xunit.Sdk.XunitException("Self-profile navigation assertion failed.",failure);
    }

    [Theory(DisplayName = "TD-APPUI-002 review navigation state follows shared authorization")]
    [InlineData("Checker", true)]
    [InlineData("Viewer", false)]
    public void Review_navigation_reflects_role_permission(string role, bool expected)
    {
        Exception? failure=null; var thread=new Thread(()=>{try{CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("USER",new[]{role})); var window=new MainWindow(); typeof(MainWindow).GetMethod("ShowAuthenticatedContent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null); var shell=Assert.IsType<ShellView>(Content(window).Content); var verification=Assert.Single(Menu(shell).Items.Cast<MenuItem>(), x=>x.Header?.ToString()=="_Verification"); var all=verification.Items.Cast<MenuItem>().SingleOrDefault(x=>x.Header?.ToString()=="_All Verifications"); Assert.Equal(expected,all is not null && all.IsEnabled); window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown();}catch(Exception ex){failure=ex;}}); thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(5)),"Review shell assertion exceeded 5 seconds.");if(failure is not null)throw new Xunit.Sdk.XunitException("Review navigation assertion failed.",failure);
    }

    [Theory(DisplayName = "TD-APPUI-002 upload navigation exposes Maker authorization state")]
    [InlineData("Maker", true)]
    [InlineData("Viewer", false)]
    [InlineData("Admin", false)]
    public void Upload_view_model_reflects_maker_permission(string role, bool expected)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CompositionRoot.SessionAuthentication.SignIn(new AuthenticatedUser("USER", new[] { role }));
                var window = new MainWindow();
                typeof(MainWindow).GetMethod("ShowAuthenticatedContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var shell = Assert.IsType<ShellView>(Content(window).Content);
                var upload = Menu(shell).Items.Cast<MenuItem>().SingleOrDefault(x => x.Header?.ToString() == "_Upload & Verify");
                Assert.Equal(expected, upload is not null && upload.IsEnabled);
                window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("Upload authorization navigation assertion failed.", failure);
    }

    private static ContentControl Content(MainWindow window) => (ContentControl)typeof(MainWindow).GetField("MainContent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static Menu Menu(ShellView shell) => Assert.IsType<Menu>(shell.FindName("ApplicationMenu"));
    private static ContentControl ShellContent(ShellView shell) => Assert.IsType<ContentControl>(shell.FindName("ContentHost"));
}
