using DrawVerify.Core.Interfaces;

namespace DrawVerify.Infrastructure.Logging;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Centralized logging sink aggregating LogEntry records.
/// </summary>
public sealed class CentralizedLogger : IAuditLogger
public sealed class CentralizedLogger : ICentralizedLogger
{
<<<<<<< HEAD
    private readonly List<AuditEntry> _entries = new();
    public IReadOnlyList<AuditEntry> Entries => _entries;
    public void Append(AuditEntry entry) => _entries.Add(entry);
=======
    public void Log(Exception exception, string operation, string context) => System.Diagnostics.Trace.TraceError($"{operation}: {context}: {exception}");
>>>>>>> 642060c (KAN-19: implementation)
}
