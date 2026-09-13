using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void ConfigureLogging()
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var configuredPath = document.RootElement.GetProperty("Logging").GetProperty("File").GetString();
            if (string.IsNullOrWhiteSpace(configuredPath)) throw new InvalidOperationException("Logging.File is empty.");

            var logPath = Path.IsPathFullyQualified(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);
            CentralizedLogger.ConfigureFile(logPath);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Logging initialization failed: {exception}");
            MessageBox.Show(
                "DrawVerify could not initialize its log file. Diagnostics may not be saved.",
                "Logging unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
