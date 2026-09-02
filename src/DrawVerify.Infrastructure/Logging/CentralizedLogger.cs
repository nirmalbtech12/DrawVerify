namespace DrawVerify.Infrastructure.Logging;

/// <summary>
/// Centralized logging sink aggregating LogEntry records.
/// </summary>
public sealed class CentralizedLogger
{
    public void Log(Exception exception, string operation, string context) => System.Diagnostics.Trace.TraceError($"{operation}: {context}: {exception}");
}
