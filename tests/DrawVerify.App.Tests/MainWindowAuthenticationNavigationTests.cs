using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawVerify.App.Services;
using DrawVerify.App.Views;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class MainWindowAuthenticationNavigationTests
{
    [Fact(DisplayName = "TD-WPF-NAV-01 starts at login and guards protected routes")]
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

    private static ContentControl Content(MainWindow window) => (ContentControl)typeof(MainWindow).GetField("MainContent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
