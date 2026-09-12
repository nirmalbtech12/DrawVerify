using System.Windows;
using System.Windows.Threading;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        new CentralizedLogger().Log(e.Exception, "WPF dispatcher", "An unhandled UI exception was intercepted; the application remained running.");
        e.Handled = true;

        try
        {
            MessageBox.Show(
                "The requested operation could not be completed. The error was logged. You can continue using the application or retry the operation.",
                "DrawVerify error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The dispatcher is already in an exceptional state; do not raise a
            // second exception while presenting the recovery message.
        }
    }
}
