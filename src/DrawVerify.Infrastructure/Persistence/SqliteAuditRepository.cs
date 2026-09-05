using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for audit entries, implementing IAuditLogger.
/// </summary>
public sealed class SqliteAuditRepository : IAuditLogger
{
    private readonly List<AuditEntry> _entries = new();
    public IReadOnlyList<AuditEntry> Entries => _entries;
    public void Append(AuditEntry entry) => _entries.Add(entry);
}
