using DrawVerify.Core.Interfaces;
using System.Diagnostics;
using System.IO;

namespace DrawVerify.Infrastructure.Logging;

/// <summary>
/// Centralized logging sink aggregating LogEntry records.
/// </summary>
public sealed class CentralizedLogger : ICentralizedLogger
{
    private static readonly object ConfigurationLock = new();
    private static TextWriterTraceListener? _fileListener;

    public static void ConfigureFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A log file path is required.", nameof(path));

        lock (ConfigurationLock)
        {
            if (_fileListener is not null) return;

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("The log file path has no directory.");

            Directory.CreateDirectory(directory);
            _fileListener = new TextWriterTraceListener(fullPath);
            _fileListener.TraceOutputOptions = TraceOptions.DateTime | TraceOptions.ThreadId;
            Trace.Listeners.Add(_fileListener);
            Trace.AutoFlush = true;
        }
    }

    public void Log(Exception exception, string operation, string context) =>
        Trace.TraceError($"{operation}: {context}: {exception}");
}
