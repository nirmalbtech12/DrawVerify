namespace DrawVerify.Infrastructure.Logging;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Centralized logging sink aggregating LogEntry records.
/// </summary>
public sealed class CentralizedLogger : IAuditLogger
{
    private readonly List<AuditEntry> _entries = new();
    public IReadOnlyList<AuditEntry> Entries => _entries;
    public void Append(AuditEntry entry) => _entries.Add(entry);
}
